# Copilot Instructions

## Project Guidelines
- Use EF Core's ValueGenerator pattern instead of SaveChanges overrides for auto-generated values
- When modifying a file, only show the specific sections that were changed (e.g. the template block or script block), not the entire file content.
- The project uses trimming (PublishTrimmed), so all JSON-serialized types must be registered via `AppJsonContext` (a `JsonSerializerContext` derived type) with `[JsonSerializable]` attributes for source-generated serialization. Do not use `JsonSerializer` with runtime-generated contracts. Always add new JSON model types to `AppJsonContext`. 