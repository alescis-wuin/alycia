# Testing strategy

The foundation uses xUnit v3 with Microsoft Testing Platform.

Current automated coverage includes:

- architectural dependency boundaries;
- presentation ViewModel baseline behavior;
- repository tooling self-tests inherited from the proven reference workflow.

Future feature packages add behavior-focused Domain/Application tests first, then adapter integration tests and UI end-to-end tests for critical chat flows.
