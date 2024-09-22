# ppUnit Connector

The ppUnit connector is a set of actions that aid you in creating tests in Power Automate for your custom connectors. This connector relies on a code script to provide all functionality and does not call any external APIs.

## Prerequisites

## Supported Actions

As part of this sample following actions are supported:

### Miscellaneous Helpers

* `Get Results Url`: Returns the URL for the result of running a given Flow. This is useful for recording the results of a test run, and linking directly to the run history.

### Test Assertions

* `Assert Equal`: Asserts that an actual value equals the expected value. The action returns an http status code and message for pass or fail conditions.
