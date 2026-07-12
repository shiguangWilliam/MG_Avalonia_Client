using Xunit;

namespace ClientAvalonia.Tests;

/// <summary>
/// xUnit collection that forces serial execution. Used for test classes that mutate
/// process-wide static state (e.g. <c>ProgramConstants.CNCNET_PROTOCOL_REVISION</c>,
/// <c>ProgramConstants.GAME_VERSION</c>) so they don't race against each other.
/// </summary>
/// <remarks>
/// No collection fixture is needed — this exists purely for the serialization attribute.
/// </remarks>
[CollectionDefinition("ProgramConstantsSerial")]
public sealed class ProgramConstantsSerialCollection
{
}
