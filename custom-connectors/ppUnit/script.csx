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

    private (string, double?) ValidateAndGetComparisonValue(Assertion assertion)
    {
        if(payload[assertion.LeftExpression].Type == JTokenType.Integer
            || payload[assertion.LeftExpression].Type == JTokenType.Float)
        {
            // Using double is kind of a cheat since it can represent all the numeric types (int/float/double). 
            // Hopefully this does not lead to any odd behavior or edge cases due to double floating point rounding errors.
            // Decimal would be more precise in representing both whole numbers and fractions, but then how do you decide on precision of the variable
            // you return from this method if it's an irrational number (neverending decimal)?
            return (null as string, double.Parse(assertion.RightExpression));
        }
        else
        {
            return ($"Numeric comparison operators require LeftExpression to refer to a property in the payload of numeric type and RightExpression must be a string that represents a numeric type.", null as double?);
        }
    }

    private (string[], bool) AssertAll(Assertion assertion)
    {
        throw new NotImplementedException(
            @"If we are going to support chaining and nesting of assertions we will have to support 
            some type of nesting or recursion to traverse the tree and aggregate the result;");
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
            // finish this later.
            // if(assertion.Assertions != null
            //     && assertion.Assertions.Any())
            // {
            //     AssertAll(assertion);
            // }

            var success = false;
            var invalidAssertionError = null as string;
            double? comparisonValue;
            switch(input.Operator?.ToLower())
            {
                case "lessthan":
                case "<":
                    (invalidAssertionError, comparisonValue) = ValidateAndGetComparisonValue(assertion);
                    if(invalidAssertionError == null)
                    {
                        success = payload[assertion.LeftExpression].Value<double>() < compareToValue;
                    }
                    break;
                case "lessthanorequalto":
                case "<=";
                    (invalidAssertionError, comparisonValue) = ValidateAndGetComparisonValue(assertion);
                    if(invalidAssertionError == null)
                    {
                        success = payload[assertion.LeftExpression].Value<double>() <= compareToValue;
                    }
                    break;
                case "greaterthan":
                case ">":
                    (invalidAssertionError, comparisonValue) = ValidateAndGetComparisonValue(assertion);
                    if(invalidAssertionError == null)
                    {
                        success = payload[assertion.LeftExpression].Value<double>() > compareToValue;
                    }
                    break;
                case "greaterthanorequalto":
                case ">=":
                    (invalidAssertionError, comparisonValue) = ValidateAndGetComparisonValue(assertion);
                    if(invalidAssertionError == null)
                    {
                        success = payload[assertion.LeftExpression].Value<double>() >= compareToValue;
                    }
                    break;
                case "equalto":
                case "==":
                    throw new NotImplementedException("This is only supported for Integer, Float, String and Bool types");
                    if(payload[assertion.LeftExpression].Type == JTokenType.String)
                    {
                        success = payload[assertion.LeftExpression].Value<string>() == assertion.RightExpression;
                    }
                    else if(payload[assertion.LeftExpression].Type == JTokenType.Int
                        || payload[assertion.LeftExpression].Type == JTokenType.Float)
                    {
                        success = payload[assertion.LeftExpression].Value<double>() == double.Parse(assertion.RightExpression);
                    }
                    else if(payload[assertion.LeftExpression].Type == JTokenType.Boolean)
                    {
                        success = payload[assertion.LeftExpression].Value<bool>() == bool.Parse(assertion.RightExpression);
                    }
                    else
                    {
                        invalidAssertionError = $"Type of LeftExpression property is not supported. Must be string, int, float or bool.";
                    }
                    break;
                case "is":
                    switch(assertion.RightExpression?.ToLower())
                    {
                        case "null":
                            success = payload[assertion.LeftExpression].Type == JTokenType.Null;
                            break;
                        case "empty":
                            success = (payload[assertion.LeftExpression].Type == JTokenType.Array && payload[assertion.LeftExpression].Any())
                            || (payload[assertion.LeftExpression].Type == JTokenType.String && !payload[assertion.LeftExpression].ToString().Any());
                        default:
                            invalidAssertionError = $"The Is operator only supports the keyword \"null\" as the RightExpression";
                    }
                    break;
                case "istype":
                    JTokenType type;
                    switch(assertion.RightExpression.ToLower())
                    {
                        case "bool":
                            type = JTokenType.Boolean;
                            break;
                        case "string":
                            type = JTokenType.String;
                            break;
                        case "int":
                            type = JTokenType.Integer;
                            break;
                        case "float":
                            type = JTokenType.Float;
                        case "object":
                            type = JTokenType.Object;
                            break;
                        case "array":
                            type = JTokenType.Array;
                            break;
                        case "null":
                            type = JTokenType.Null;
                            break;
                        default:
                            invalidAssertionError = $"The IsType operator only supports the keywords [\"bool\", \"string\", \"int\", \"float\", \"object\", \"array\", \"null\"] as the RightExpression.";
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

            throw new NotImplementedException("Logical operators and grouping not implemented. See comment block below");
            // If we add support for grouping assertions by logical operator (AND/OR) this would be where you'd need to use that operator.
            // Also might need to consider when setting the initial value of totalSuccess whether it should start as...
            // - False (OR operator)
            // - True (AND operator)
            //
            // This is also probably where you might decide to abort and just return the result if the user has specified a short circuiting logical operator (ANDD/ORR)
            //
            // In addition to the other changes noted here, there needs to be some sort of loop or recursion at high level that handles
            // the execution and aggregation of each logical grouping of assertions into the final result. And also correctly collects an array of feedback messages if assertions fail...
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
        // Valid Values ["OR", "ORR", "AND", "ANDD"]
        // how to chain together the results of each individual Assertion in the Assertions array.
        public string LogicalOperator { get ; set; }
        public Assertion[] Assertions { get; set; }


        // Really if the top two properties are not null, then these bottom properties should not be filled in.
        public string LeftExpression { get; set; }
        public string RightExpression { get; set; }
        public string Operator { get ;set; }
        public string ErrorMessage { get; set; }
    }

    public class AssertAllResult()
    {
        public bool Passed { get; set; }
        public string[] ErrorMessages { get; set; }
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