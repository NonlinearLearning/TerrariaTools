using Analysis.Frontend;
using Xunit;

namespace Analysis.Tests.Frontend;

public sealed class SymbolTableTests
{
    [Fact]
    public void Append_prioritizesConcreteTypesBeforeDummyTypes()
    {
        SymbolTable<LocalVariableKey> table = new(typeFullName => typeFullName.StartsWith("ANY", StringComparison.Ordinal));
        LocalVariableKey key = new("value");

        table.Append(key, ["ANY<number>", "System.Int32"]);

        IReadOnlyCollection<string> values = table.Get(key);
        Assert.Equal(["System.Int32", "ANY<number>"], values);
    }

    [Fact]
    public void Put_replacesExistingValues()
    {
        SymbolTable<LocalVariableKey> table = new();
        LocalVariableKey key = new("value");

        table.Put(key, ["System.Int32"]);
        table.Put(key, ["System.String"]);

        Assert.Equal(["System.String"], table.Get(key));
    }
}
