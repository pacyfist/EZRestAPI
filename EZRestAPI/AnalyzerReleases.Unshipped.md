; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EZR001 | EZRestAPI | Error | Model class must be partial
EZR002 | EZRestAPI | Error | Duplicate model singular name
EZR003 | EZRestAPI | Error | Duplicate model plural name
EZR004 | EZRestAPI | Error | Model type used as a navigation property
EZR005 | EZRestAPI | Error | Nested model cycle
EZR006 | EZRestAPI | Error | Duplicate nested singular name
EZR007 | EZRestAPI | Error | Unsupported Id property type
EZR008 | EZRestAPI | Error | Name is not a valid identifier
EZR009 | EZRestAPI | Error | Unsupported container for a nested model
EZR010 | EZRestAPI | Error | Class is both a model and a nested model
EZR011 | EZRestAPI | Warning | Foreign-key-shaped property has no matching model
