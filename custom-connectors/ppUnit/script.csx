using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using SnowflakeConnectorConsoleApp;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Net;
using System;

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
            case "AssertAll":
                return GetAssertAllResponse(originalContent);
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

    private (string, double?) ValidateAndGetComparisonValue(JObject payload, Assertion assertion)
    {
        if (payload[assertion.LeftExpression].Type == JTokenType.Integer
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

    private AssertAllResult AssertAll(JObject payload, Assertion topLevelAssertion)
    {
        var currentResult = new AssertAllResult();
        var result = new AssertAllResult();

        var shortCircuitCondition = null as bool?;
        Func<bool, bool, bool> comparisonFunc;
        switch (topLevelAssertion.LogicalOperator.ToUpper())
        {
            case "ANDD":
            case "&&":
                shortCircuitCondition = false;
                result.Passed = true;
                comparisonFunc = Andd;
                break;
            case "AND":
            case "&":
                shortCircuitCondition = false;
                result.Passed = true;
                comparisonFunc = And;
                break;
            case "ORR":
            case "||":
                shortCircuitCondition = true;
                result.Passed = false;
                comparisonFunc = Orr;
                break;
            case "OR":
            case "|":
                shortCircuitCondition = false;
                result.Passed = false;
                comparisonFunc = Or;
                break;
            default:
                throw new NotImplementedException("Need to handle this error as invalid LogicalOperator.");
        };

        var index = 0;
        foreach (var assertion in topLevelAssertion.Assertions)
        {
            var invalidAssertionError = null as string;
            if (assertion.LogicalOperator != null)
            {
                currentResult = AssertAll(payload, assertion);
            }
            else
            {
                double? comparisonValue;

                switch (assertion.Operator?.ToLower())
                {
                    case "lessthan":
                    case "<":
                        (invalidAssertionError, comparisonValue) = ValidateAndGetComparisonValue(payload, assertion);
                        if (invalidAssertionError == null)
                        {
                            currentResult.Passed = payload[assertion.LeftExpression].Value<double>() < comparisonValue;
                        }
                        break;
                    case "lessthanorequalto":
                    case "<=":
                        (invalidAssertionError, comparisonValue) = ValidateAndGetComparisonValue(payload, assertion);
                        if (invalidAssertionError == null)
                        {
                            currentResult.Passed = payload[assertion.LeftExpression].Value<double>() <= comparisonValue;
                        }
                        break;
                    case "greaterthan":
                    case ">":
                        (invalidAssertionError, comparisonValue) = ValidateAndGetComparisonValue(payload, assertion);
                        if (invalidAssertionError == null)
                        {
                            currentResult.Passed = payload[assertion.LeftExpression].Value<double>() > comparisonValue;
                        }
                        break;
                    case "greaterthanorequalto":
                    case ">=":
                        (invalidAssertionError, comparisonValue) = ValidateAndGetComparisonValue(payload, assertion);
                        if (invalidAssertionError == null)
                        {
                            currentResult.Passed = payload[assertion.LeftExpression].Value<double>() >= comparisonValue;
                        }
                        break;
                    case "equalto":
                    case "equals":
                    case "==":
                        if (payload[assertion.LeftExpression].Type == JTokenType.String)
                        {
                            currentResult.Passed = payload[assertion.LeftExpression].Value<string>() == assertion.RightExpression;
                        }
                        else if (payload[assertion.LeftExpression].Type == JTokenType.Integer
                            || payload[assertion.LeftExpression].Type == JTokenType.Float)
                        {
                            currentResult.Passed = payload[assertion.LeftExpression].Value<double>() == double.Parse(assertion.RightExpression);
                        }
                        else if (payload[assertion.LeftExpression].Type == JTokenType.Boolean)
                        {
                            currentResult.Passed = payload[assertion.LeftExpression].Value<bool>() == bool.Parse(assertion.RightExpression);
                        }
                        else
                        {
                            invalidAssertionError = $"Type of LeftExpression property is not supported. Must be string, int, float or bool.";
                        }
                        break;
                    case "is":
                        switch (assertion.RightExpression?.ToLower())
                        {
                            case "null":
                                currentResult.Passed = payload[assertion.LeftExpression].Type == JTokenType.Null;
                                break;
                            case "empty":
                                currentResult.Passed = (payload[assertion.LeftExpression].Type == JTokenType.Array && !payload[assertion.LeftExpression].Any())
                                || (payload[assertion.LeftExpression].Type == JTokenType.String && !payload[assertion.LeftExpression].ToString().Any());
                                break;
                            default:
                                invalidAssertionError = $"The Is operator only supports the keyword \"null\" as the RightExpression";
                                break;
                        }
                        break;
                    case "istype":
                        JTokenType? type = null;
                        switch (assertion.RightExpression.ToLower())
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
                                break;
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
                                break;
                        }

                        if (type != null)
                        {
                            currentResult.Passed = payload[assertion.LeftExpression].Type == type;
                        }
                        break;
                    default:
                        invalidAssertionError = $"The Operator \"{assertion.Operator}\" is not supported";
                        break;
                }

                if (assertion.Negate)
                {
                    currentResult.Passed = !currentResult.Passed;
                }


                // this type of error overrides any sort of assertion result b/c it denotes that one of the assertions is invalid.
                if (invalidAssertionError != null)
                {
                    currentResult.Passed = false;
                    currentResult.StatusCode = HttpStatusCode.BadRequest;
                }

                if (!currentResult.Passed)
                {
                    // an errorOverride is an error that is either syntactic or otherwise indicates 
                    // that the assertion itself is invalid and cannot be evaluated.
                    if (invalidAssertionError == null)
                    {
                        currentResult.ErrorMessages.Add($"Assertion[{index}] failed. {assertion.ErrorMessage}");
                    }
                    else
                    {
                        currentResult.ErrorMessages.Add($"Assertion[{index}] invalid. {invalidAssertionError}");
                    }

                }
            }
            
            // mesh current result with previously aggregated results.
            result.Passed = comparisonFunc(result.Passed, currentResult.Passed);
            result.StatusCode = GetWorseStatusCode(currentResult.StatusCode, result.StatusCode);
            if (currentResult.ErrorMessages.Any())
            {
                result.ErrorMessages.AddRange(currentResult.ErrorMessages);
            }

            if (shortCircuitCondition.HasValue 
                && result.Passed == shortCircuitCondition)
            {
                // pull the parachute!
                break;
            }

            index++;
        }
    
        // kludgey, but if the net resuilt is a pass then we shouldn't be returning errors.
        if (result.Passed)
        {
            result.ErrorMessages.Clear();
        }
        return result;
    }

    private HttpStatusCode GetWorseStatusCode(HttpStatusCode code1, HttpStatusCode code2)
    {
        // this is so naive but mostly works for escalating error levels.
        return (HttpStatusCode)(Math.Max((int)code1, (int)code2));
    }

    private bool And(bool t1, bool t2) => t1 & t2;
    private bool Andd(bool t1, bool t2) => t1 && t2;
    private bool Or(bool t1, bool t2) => t1 | t2;
    private bool Orr(bool t1, bool t2) => t1 || t2;

    private HttpResponseMessage GetAssertAllResponse(string content)
    {
        var input = JsonConvert.DeserializeObject<AssertAllPayload>(content);
        var payload = JObject.Parse(input.Payload);

        var result = AssertAll(payload, input.Assertion);

        var response = new HttpResponseMessage(result.StatusCode);

        response.Content = CreateJsonContent(JsonConvert.SerializeObject(new
        {
            result,
        }));

        return response;
    }

    #region Object Models
    public class AssertAllPayload
    {
        // This parameter must actually factually come in as a string in the request body or the parsing will fail
        // There must be a slicker way that we could allows this to be a class based parameter in the request body,
        // but haven't figured it out yet.
        public string Payload { get; set; }
        public Assertion Assertion { get; set; }
    }

    /// <summary>
    /// Valid Assertion Operators:
    /// "Equals" - compares properties in payload as specified by the Left/RightExpression.
    /// "Is"  - compares property in payload as specified by LeftExpression and compares to literal specified in RightExpression. The only supported keyword currently is "null" or "empty".
    /// "IsType" - compares the type of the property in payload as specified by LeftExpression and compares to type literal specified in RightExpression. The only supported keywords are "bool", "string", "int", "object" and "array".
    /// </summary>
    public class Assertion
    {
        // Valid Values ["OR", "ORR", "AND", "ANDD"]
        // how to chain together the results of each individual Assertion in the Assertions array.
        public string LogicalOperator { get; set; }
        public Assertion[] Assertions { get; set; }


        // Really if the top two properties are not null, then these bottom properties should not be filled in.
        public string LeftExpression { get; set; }
        public string RightExpression { get; set; }
        public string Operator { get; set; }
        public string ErrorMessage { get; set; }
        public bool Negate { get; set; }
    }

    public class AssertAllResult
    {
        public bool Passed { get; set; }
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public List<string> ErrorMessages { get; set; } = new List<string>();
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