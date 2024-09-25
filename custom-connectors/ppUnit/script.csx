public class Script : ScriptBase
{
    private readonly HttpResponseMessage OK_RESPONSE;

    public Script()
    {
        OK_RESPONSE = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(JsonConvert.SerializeObject(new { statusCode = 200, statusMessage = "OK" }))
        };
    }

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var originalContent = await Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        Context.Logger.LogInformation($"Operation: '{Context.OperationId}'");

        switch (Context.OperationId)
        {
            case "GetResultsUrl":
                return GetResultUrl(originalContent);
            case "AssertEqual":
                return GetAssertEqual(originalContent);
            default:
                return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

    }

    private HttpResponseMessage GetResultUrl(string content)
    {
        var flowJson = JsonConvert.DeserializeObject<WorkflowRoot>(content).workflow;
        var flow = JsonConvert.DeserializeObject<Workflow>(flowJson);
        var url = $"https://flow.microsoft.com/manage/environments/{flow?.tags?.environmentName}/flows/{flow?.name}/runs/{flow?.run?.name}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(JsonConvert.SerializeObject(new { resultUrl = url }))
        };
    }

    /// <summary>
    /// Asserts that the actual value is equal to the expected value.
    /// </summary>
    /// <param name="content">The JSON object representing the input parameters for this action</param>
    /// <returns></returns>
    private HttpResponseMessage GetAssertEqual(string content)
    {
        var input = JsonConvert.DeserializeObject<AssertEqualInput>(content);

        if (input.actual == null && input.expected == null)
        {
            return OK_RESPONSE;
        }

        if (input.actual == null || input.expected == null || !input.actual.Equals(input.expected))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent(JsonConvert.SerializeObject(new
                {
                    statusCode = input.failureCode,
                    statusMessage = input.failureMessage
                }))
            };
        }

        return OK_RESPONSE;
    }

    private HttpResponseMessage GetAssertAll(string content)
    {
        var input = JsonConvert.DeserializeObject<AssertAllPayload>(content);
        var payload = JObject.Parse(input.Payload);
        var totalSuccess = true;
        var errors = new List<string>();
        var index = 0;
        var statusCode = HttpStatusCode.OK;
        foreach(var assertion in input.Assertions)
        {
            var success = false;
            var invalidAssertionError = null as string;
            switch(input.Operator?.ToLower())
            {
                case "equals":
                    success = payload[assertion.LeftExpression] == payload[assertion.RightExpression];
                    break;
                case "is":
                    switch(assertion.RightExpression?.ToLower())
                    {
                        case "null":
                            success = payload[assertion.LeftExpression].Type == JObjectType.Null;
                            break;
                        case "empty":
                            success = (payload[assertion.LeftExpression].Type == JObjectType.Array && payload[assertion.LeftExpression].Any())
                            || (payload[assertion.LeftExpression].Type == JObjectType.String && !payload[assertion.LeftExpression].ToString().Any());
                        default:
                            invalidAssertionError = $"The Is operator only supports the keyword \"null\" as the RightExpression";
                    }
                    break;
                case "istype":
                    JObjectType type;
                    switch(assertion.RightExpression.ToLower())
                    {
                        case "bool":
                            type = JObjectType.Boolean;
                            break;
                        case "string":
                            type = JObjectType.String;
                            break;
                        case "float":
                            type = JObjectType.Float;
                        case "object":
                            type = JObjectType.Object;
                            break;
                        case "array":
                            type = JObjectType.Array;
                            break;
                        case "null":
                            type = JObjectType.Null;
                            break;
                        default:
                            invalidAssertionError = $"The IsType operator only supports the keywords [\"bool\", \"string\", \"object\", \"array\"] as the RightExpression.";
                    }

                    if(type != null)
                    {
                        success = payload[assertion.LeftExpression].Type == type;
                    }
                    break;
                case default:
                    invalidAssertionError = $"The Operator "{assertion.Operator}"" is not supported";
            }

            if(invalidAssertionError != null)
            {
                success = false;
                statusCode = HttpStatusCode.BadRequest;
            }

            if(!success)
            {
                totalSuccess = false;
                // an errorOverride is an error that is either syntactic or otherwise indicates 
                // that the assertion itself is invalid and cannot be evaluated.
                if(invalidAssertionError == null)
                {
                    errors.Add($"Assertion[{index}] failed. {assertion.ErrorMessage}");
                }
                else
                {
                    errors.Add($"Assertion[{index}] invalid. {invalidAssertionError}");
                }
            }
            index++;
        }

        var response =  new HttpResponseMessage(statusCode);
        
        response.Content = Content = CreateJsonContent(JsonConvert.SerializeObject(new
        {
            new AssertAllResult()
            {
                Passed = totalSuccess,
                ErrorMessages = errors.ToArray(),
            },
        };
    }

    #region Object Models
    public class AssertAllPayload()
    {
        public string Payload { get; set; }
        public Assertion[] Assertions { get; set; }
    }

    /// <summary>
    /// Valid Assertion Operators:
    /// "Equals" - compares properties in payload as specified by the Left/RightExpression.
    /// "Is"  - compares property in payload as specified by LeftExpression and compares to literal specified in RightExpression. The only supported keyword currently is "null".
    /// "IsType" - compares the type of the property in payload as specified by LeftExpression and compares to type literal specified in RightExpression. The only supported keywords are "bool", "string", "int", "object" and "array".
    /// </summary>
    public class Assertion()
    {
        public string LeftExpression { get; set; }
        public string RightExpression { get; set; }
        public string Operator { get ;set; }
        public string ErrorMessage {get;set;}
    }

    public class AssertAllResult()
    {
        public bool Passed { get; set; }
        public string[] ErrorMessages { get ;set; }
    }

    public class AssertEqualInput
    {
        public string actual { get; set; }
        public string expected { get; set; }
        public decimal failureCode { get; set; }
        public string failureMessage { get; set; }
    }

    public class WorkflowRoot
    {
        public string workflow { get; set; }
    }

    public class Workflow
    {
        public string id { get; set; }
        public string name { get; set; }
        public string type { get; set; }
        public string location { get; set; }
        public Tags tags { get; set; }
        public Run run { get; set; }
    }

    public class Run
    {
        public string id { get; set; }
        public string name { get; set; }
        public string type { get; set; }
    }

    public class Tags
    {
        public string flowDisplayName { get; set; }
        public string environmentName { get; set; }
        public string logicAppName { get; set; }
        public string environmentWorkflowId { get; set; }
        public string xrmWorkflowId { get; set; }
        public string environmentFlowSuspensionReason { get; set; }
        public string sharingType { get; set; }
        public string state { get; set; }
        public string createdTime { get; set; }
        public string lastModifiedTime { get; set; }
        public string createdBy { get; set; }
        public string triggerType { get; set; }
    }
    #endregion
}