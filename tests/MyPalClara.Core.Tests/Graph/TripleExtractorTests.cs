using MyPalClara.Modules.Graph.Models;

namespace MyPalClara.Core.Tests.Graph;

public class TripleExtractorTests
{
    [Fact]
    public void GraphTriple_CanConstruct()
    {
        var triple = new GraphTriple("Alice", "knows", "Bob");
        Assert.Equal("Alice", triple.Subject);
        Assert.Equal("knows", triple.Predicate);
        Assert.Equal("Bob", triple.Object);
    }

    [Fact]
    public void GraphEntity_CanConstruct()
    {
        var entity = new GraphEntity("1", "Alice", "person");
        Assert.Equal("1", entity.Id);
        Assert.Equal("Alice", entity.Name);
        Assert.Equal("person", entity.Type);
    }

    [Fact]
    public void GraphRelationship_CanConstruct()
    {
        var rel = new GraphRelationship("r1", "1", "2", "knows");
        Assert.Equal("r1", rel.Id);
        Assert.Equal("1", rel.SourceId);
        Assert.Equal("2", rel.TargetId);
        Assert.Equal("knows", rel.Type);
    }
}
