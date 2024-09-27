# ppUnit Connector

The ppUnit connector is a set of actions that aid you in creating tests in Power Automate for your custom connectors. This connector relies on a code script to provide all functionality and does not call any external APIs.

## Prerequisites

## Supported Actions

As part of this sample following actions are supported:

### Miscellaneous Helpers

* `Get Results Url`: Returns the URL for the result of running a given Flow. This is useful for recording the results of a test run, and linking directly to the run history.

### Test Assertions

* `Assert All`: Chains together a list of assertions that .

    - Assertion Schema:
        - LogicalOperator: Determines how to chain the outcome of the individual nested Assertions. This is required for the top level assertion.
            - ANDD/&&: Short circuiting and.
            - AND/&: Non-short circuiting and.
            - ORR/||: Short circuiting or.
            - OR/|: Non-short circuiting or.
        - Assertions: A collection of assertions to be evaluated and chained together using the "Logical Operator" property. This may only be specified if the LogicalOperator property is specified.
            - Notes that this definition is recursive, meaning that every assertion in this collection is of the very same type described here. This means that you can nest groups of assertions as deeply as needed.
        - Operator (aka Assertion Operator)
            - is:
                - LeftExpression: literal value to compare.
                - RightExpressiong: ["null","empty"]
            - equalto/equals/==: 
                - LeftExpression: literal value to compare.
                - RightExpression: literal value to compare.
            - lessthan/<:
                - LeftExpression: literal value to compare.
                - RightExpression: literal value to compare.
            - lessthanorequalto/<=:
                - LeftExpression: literal value to compare.
                - RightExpression: literal value to compare.
            - greaterthan/>:
                - LeftExpression: literal value to compare.
                - RightExpression: literal value to compare.
            - greaterthanorequalto/>=:
                - LeftExpression: literal value to compare.
                - RightExpression: literal value to compare.
            - istype
                
                - LeftExpression: literal value to compare (altho the type of the value is more important).
                - RightExpression: type of object. Valid values ["bool","string","int","float","object","array","null"].
                    - "null" asserts if the value is null. It's a remnant of how Newtonsoft/JToken represents nulls. Note that because this is all JSON, attempting to accurately assert the type of a null (JSON has no provision for "null as int" or "null as string")
        - Left Expression: A literal value to be evaluated by the assertion.
        - Right Expression: See "Operator" definition. Depending on the operator, this could be a literal for comparison or a keyword to indicate behavior of the operator.
        - Negate - Effectivley a "not operator in front of your expression.
            - true: Negate the outcome of the assertion.
            - false: Do nothing.
        - ErrorMessage: message to return if assertion fails.

    - Note: There is a concept of a "top level' or "grouping" assertion in which only the LogicalOperator and Assertions properties are specified. There is also the notiion of a "bottom level" assertion in which LogicalOperator/Assertion must be null, but the Operator/LeftExpression/RightExpression/Message are specified. The reason for this is because this object is recursive and allows multiple 

    - Sample payloads:
        - Assert that values is not null, is of type integer and has a positive value.
        <br/><b>c# equiv:</b> value != null && value.GetType() == typeof(int) && value > 0
            ```
            {
                "Assertion": {
                    "LogicalOperator": "ANDD",
                    "Assertions": [
                        {
                        "LeftExpression": 17,
                        "Operator": "is",
                        "Negate": true,
                        "RightExpression": "null",
                        "ErrorMessage": "value should not be null"
                        },
                        {
                        "LeftExpression": 17,
                        "Operator": "istype",
                        "Negate": false,
                        "RightExpression": "int",
                        "ErrorMessage":  "value is not of expected type string"
                        },
                        {
                        "LeftExpression": 17,
                        "Operator": "greaterthan",
                        "Negate": false,
                        "RightExpression": 0,
                        "ErrorMessage":  "value should be a positive number"
                        }
                    ]
                }
            }
            ```

        - (value1 is not null and is of type string) or (value2 is not null and is of type int)
        <br/><b>c# equiv:</b> (value1 != null && value1.GetType() == typeof(string)) || (value2 != null && value2.GetType() == typeof(int))
            ```
            {
                "Assertion": {
                    "LogicalOperator": "ORR",
                    "Assertions": [
                        {
                            "LogicalOperator": "ANDD",
                            "Assertions": [
                                {
                                    "LeftExpression": "a string value",
                                    "Operator": "is",
                                    "Negate": true,
                                    "RightExpression": "null",
                                    "ErrorMessage": "value1 should not be null"
                                },
                                {
                                    "LeftExpression": "a string value",
                                    "Operator": "istype",
                                    "Negate": false,
                                    "RightExpression": "string",
                                    "ErrorMessage":  "value1 is not of expected type string"
                                }
                            ]
                        },
                        {
                            "LogicalOperator": "ANDD",
                            "Assertions": [
                            {
                                "LeftExpression": 24,
                                "Operator": "is",
                                "Negate": true,
                                "RightExpression": "null",
                                "ErrorMessage":  "value2 should not be null"
                            },
                            {
                                "LeftExpression": 24,
                                "Operator": "istype",
                                "Negate": false,
                                "RightExpression": "int",
                                "ErrorMessage": "value2 is not of expected type int"
                            }
                            ]
                        }
                    ]
                }
            }
            ```
All assertions will return an status code and a status message for all pass or fail conditions. All passing assertions will return the same 200/0k response. Every assertion will

* `Assert Equal`: Asserts that an actual value equals the expected value. 

* `Assert True`: Asserts that a given condition is true.

* `Assert False`: Asserts that a given condition is false.
