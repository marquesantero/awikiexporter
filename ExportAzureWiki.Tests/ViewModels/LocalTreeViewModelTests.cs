using System.ComponentModel;
using ExportAzureWiki.Wpf.ViewModels;

namespace ExportAzureWiki.Tests.ViewModels;

/// <summary>
/// Unit tests for the workspace tree view models: the recently-built checkbox
/// cascade (check a folder -> check its descendants) and the pure local-tree
/// builder that turns relative paths into a folder/file hierarchy.
/// </summary>
public sealed class LocalTreeViewModelTests
{
    // ---- LocalPageNodeViewModel ------------------------------------------

    [Fact]
    public void LocalNode_Defaults_To_Checked()
    {
        var node = new LocalPageNodeViewModel { Title = "a.md", RenderedPath = "a.md" };

        node.IsChecked.Should().BeTrue("opening a folder selects everything by default");
        node.IsFile.Should().BeTrue();
    }

    [Fact]
    public void LocalNode_Folder_Has_No_RenderedPath_And_Is_Not_File()
    {
        var folder = new LocalPageNodeViewModel { Title = "docs", RenderedPath = null };

        folder.IsFile.Should().BeFalse();
    }

    [Fact]
    public void LocalNode_Unchecking_Folder_Cascades_To_Descendants()
    {
        var folder = new LocalPageNodeViewModel { Title = "docs", RenderedPath = null };
        var child1 = new LocalPageNodeViewModel { Title = "a.md", RenderedPath = "docs/a.md", Parent = folder };
        var sub = new LocalPageNodeViewModel { Title = "sub", RenderedPath = null, Parent = folder };
        var grandchild = new LocalPageNodeViewModel { Title = "b.md", RenderedPath = "docs/sub/b.md", Parent = sub };
        folder.Children.Add(child1);
        folder.Children.Add(sub);
        sub.Children.Add(grandchild);

        folder.IsChecked = false;

        child1.IsChecked.Should().BeFalse();
        sub.IsChecked.Should().BeFalse();
        grandchild.IsChecked.Should().BeFalse("the cascade is recursive through nested folders");
    }

    [Fact]
    public void LocalNode_Raises_PropertyChanged_For_IsChecked()
    {
        var node = new LocalPageNodeViewModel { Title = "a.md", RenderedPath = "a.md" };
        var raised = new List<string?>();
        ((INotifyPropertyChanged)node).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        node.IsChecked = false;

        raised.Should().Contain(nameof(LocalPageNodeViewModel.IsChecked));
    }

    [Fact]
    public void LocalNode_Setting_Same_Value_Does_Not_Raise()
    {
        var node = new LocalPageNodeViewModel { Title = "a.md", RenderedPath = "a.md" };
        var raised = 0;
        ((INotifyPropertyChanged)node).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocalPageNodeViewModel.IsChecked))
            {
                raised++;
            }
        };

        node.IsChecked = true; // already true

        raised.Should().Be(0);
    }

    // ---- WikiPageNodeViewModel (Online tab) ------------------------------

    [Fact]
    public void WikiNode_Defaults_To_Unchecked()
    {
        var node = new WikiPageNodeViewModel { Title = "Home", Path = "/Home" };

        node.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void WikiNode_Checking_Folder_Cascades_To_Descendants()
    {
        var root = new WikiPageNodeViewModel { Title = "Root", Path = "/Root" };
        var child = new WikiPageNodeViewModel { Title = "Child", Path = "/Root/Child", Parent = root };
        var grandchild = new WikiPageNodeViewModel { Title = "Leaf", Path = "/Root/Child/Leaf", Parent = child };
        root.Children.Add(child);
        child.Children.Add(grandchild);

        root.IsChecked = true;

        child.IsChecked.Should().BeTrue();
        grandchild.IsChecked.Should().BeTrue();
    }

    // ---- BuildLocalTreeNodes (pure builder) ------------------------------

    [Fact]
    public void BuildTree_Flat_Files_Become_Leaf_Nodes()
    {
        var nodes = WorkspaceViewModel.BuildLocalTreeNodes(new[] { "a.md", "b.md" });

        nodes.Should().HaveCount(2);
        nodes.Should().OnlyContain(n => n.IsFile);
        nodes.Select(n => n.RenderedPath).Should().BeEquivalentTo("a.md", "b.md");
    }

    [Fact]
    public void BuildTree_Nested_Paths_Group_Under_Folder_Nodes()
    {
        var nodes = WorkspaceViewModel.BuildLocalTreeNodes(new[]
        {
            "docs/intro.md",
            "docs/guide/setup.md",
        });

        nodes.Should().ContainSingle();
        var docs = nodes[0];
        docs.Title.Should().Be("docs");
        docs.IsFile.Should().BeFalse();
        docs.Children.Should().HaveCount(2); // intro.md (file) + guide (folder)

        var intro = docs.Children.Single(c => c.Title == "intro.md");
        intro.IsFile.Should().BeTrue();
        intro.RenderedPath.Should().Be("docs/intro.md");
        intro.Parent.Should().BeSameAs(docs);

        var guide = docs.Children.Single(c => c.Title == "guide");
        guide.IsFile.Should().BeFalse();
        var setup = guide.Children.Should().ContainSingle().Subject;
        setup.RenderedPath.Should().Be("docs/guide/setup.md");
        setup.Parent.Should().BeSameAs(guide);
    }

    [Fact]
    public void BuildTree_Siblings_Share_One_Folder_Node()
    {
        var nodes = WorkspaceViewModel.BuildLocalTreeNodes(new[]
        {
            "docs/a.md",
            "docs/b.md",
        });

        nodes.Should().ContainSingle("both files live under the same docs folder");
        nodes[0].Children.Should().HaveCount(2);
    }

    [Fact]
    public void BuildTree_Ignores_Empty_And_Whitespace_Paths()
    {
        var nodes = WorkspaceViewModel.BuildLocalTreeNodes(new[] { "", "   ", "a.md" });

        nodes.Should().ContainSingle();
        nodes[0].RenderedPath.Should().Be("a.md");
    }
}
