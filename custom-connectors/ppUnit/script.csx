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

    #region Object Models
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