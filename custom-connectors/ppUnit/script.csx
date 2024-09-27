using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
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
        var input = JsonConvert.DeserializeObject<AssertEqualityInput>(content);

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
        if (assertion.LeftExpression.Type == JTokenType.Integer
            || assertion.LeftExpression.Type == JTokenType.Float)
        {
            // Using double is kind of a cheat since it can represent all the numeric types (int/float/double). 
            // Hopefully this does not lead to any odd behavior or edge cases due to double floating point rounding errors.
            // Decimal would be more precise in representing both whole numbers and fractions, but then how do you decide on precision of the variable
            // you return from this method if it's an irrational number (neverending decimal)?
            return (null as string, assertion.RightExpression.Value<double>());
        }
        else
        {
            return ($"Numeric comparison operators require LeftExpression and RightExpression to both be numeric", null as double?);
        }
    }

    private AssertAllResult AssertAll(Assertion topLevelAssertion)
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
                shortCircuitCondition = null;
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
                shortCircuitCondition = null;
                result.Passed = false;
                comparisonFunc = Or;
                break;
            default:
                result.Passed = false;
                result.ErrorMessages.Add("Invalid LogicalOperator. The valid values are ['AND', 'ANDD', 'OR', 'ORR']");
                result.StatusCode = HttpStatusCode.BadRequest;

                return result;
        };

        var index = 0;
        foreach (var assertion in topLevelAssertion.Assertions)
        {
            var invalidAssertionError = null as string;
            if (assertion.LogicalOperator != null)
            {
                currentResult = AssertAll(assertion);
            }
            else
            {
                double? comparisonValue;

                switch (assertion.Operator?.ToLower())
                {
                    case "lessthan":
                    case "<":
                        (invalidAssertionError, comparisonValue) = ValidateAndGetComparisonValue(assertion);
                        if (invalidAssertionError == null)
                        {
                            currentResult.Passed = assertion.LeftExpression.Value<double>() < comparisonValue;
                        }
                        break;
                    case "lessthanorequalto":
                    case "<=":
                        (invalidAssertionError, comparisonValue) = ValidateAndGetComparisonValue(assertion);
                        if (invalidAssertionError == null)
                        {
                            currentResult.Passed = assertion.LeftExpression.Value<double>() <= comparisonValue;
                        }
                        break;
                    case "greaterthan":
                    case ">":
                        (invalidAssertionError, comparisonValue) = ValidateAndGetComparisonValue(assertion);
                        if (invalidAssertionError == null)
                        {
                            currentResult.Passed = assertion.LeftExpression.Value<double>() > comparisonValue;
                        }
                        break;
                    case "greaterthanorequalto":
                    case ">=":
                        (invalidAssertionError, comparisonValue) = ValidateAndGetComparisonValue(assertion);
                        if (invalidAssertionError == null)
                        {
                            currentResult.Passed = assertion.LeftExpression.Value<double>() >= comparisonValue;
                        }
                        break;
                    case "equalto":
                    case "equals":
                    case "==":
                        if (assertion.LeftExpression.Type == JTokenType.String)
                        {
                            currentResult.Passed = assertion.LeftExpression.Value<string>() == assertion.RightExpression?.Value<string>();
                        }
                        else if (assertion.LeftExpression.Type == JTokenType.Integer
                            || assertion.LeftExpression.Type == JTokenType.Float)
                        {
                            currentResult.Passed = assertion.LeftExpression.Value<double>() == assertion.RightExpression?.Value<double>();
                        }
                        else if (assertion.LeftExpression.Type == JTokenType.Boolean)
                        {
                            currentResult.Passed = assertion.LeftExpression.Value<bool>() == assertion.RightExpression?.Value<bool>();
                        }
                        else
                        {
                            invalidAssertionError = $"Type of LeftExpression property is not supported. Must be string, int, float or bool.";
                        }
                        break;
                    case "is":
                        switch (assertion.RightExpression?.ToString()?.ToLower())
                        {
                            case "null":
                                currentResult.Passed = assertion.LeftExpression.Type == JTokenType.Null;
                                break;
                            case "empty":
                                currentResult.Passed = (assertion.LeftExpression.Type == JTokenType.Array && !((JArray)assertion.LeftExpression).Any())
                                || (assertion.LeftExpression.Type == JTokenType.String && !assertion.LeftExpression.ToString().Any());
                                break;
                            default:
                                invalidAssertionError = $"The Is operator only supports the keywords [\"null\", \"empty\"] as the RightExpression";
                                break;
                        }
                        break;
                    case "istype":
                        JTokenType? type = null;
                        switch (assertion.RightExpression?.Value<string>()?.ToLower())
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
                            currentResult.Passed = assertion.LeftExpression.Type == type;
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
                // pull the parachute, we're out!
                break;
            }

            index++;
        }

        // kludgey, but if the net result is a pass then we shouldn't be returning errors.
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

        var result = AssertAll(input.Assertion);

        var response = new HttpResponseMessage(result.StatusCode);

        response.Content = CreateJsonContent(JsonConvert.SerializeObject(result));

        return response;
    }
    private HttpResponseMessage GetAssertNotEqual(string content)
    {
        var input = JsonConvert.DeserializeObject<AssertEqualityInput>(content);

        boolean actualNull  = input.actual == null;
        boolean expectedNull = input.expected == null;

        if (actualNull ^ expectedNull)
        {
            return OK_RESPONSE;
        }

        if ((actualNull && expectedNull) || input.actual.Equals(input.expected))
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
    public class AssertAllPayload
    {
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
        // the logical operator with which to chain the output of each assertion within the Assertions array.
        public string LogicalOperator { get; set; }
        public Assertion[] Assertions { get; set; }

        // If the top two properties are not null, then these bottom properties should not be filled in.
        // Perhaps a tad clunky, but necessary b/c this is a recursive type definition.
        public JToken LeftExpression { get; set; }
        public JToken RightExpression { get; set; }
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

    public class AssertEqualityInput
    {
        public string actual { get; set; }
        public string expected { get; set; }
        public decimal failureCode { get; set; }
        public string failureMessage { get; set; }
    }

    public class AssertBooleanInput
    {
        public string actual { get; set; }
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