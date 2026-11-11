using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Schoology.WinUI;

public sealed partial class MainWindow
{
    private sealed record ParentLinkedStudent(long Id, string Name);
    private readonly List<ParentLinkedStudent> _parentChildren = [];
    private long _selectedLearningUserId;
    private string _selectedLearningUserName = string.Empty;
    private long LearningUserId => _selectedLearningUserId > 0 ? _selectedLearningUserId : _session?.UserId ?? 0;

    private async Task LoadParentChildrenAsync()
    {
        _parentChildren.Clear();
        _selectedLearningUserId = 0;
        _selectedLearningUserName = string.Empty;
        var session = _session;
        if (session is null) return;
        var ids = new HashSet<long>();
        var childValue = FindJsonPropertyRecursive(session.User, "child_uids", "childUids");
        if (childValue.ValueKind == JsonValueKind.String)
        {
            foreach (var token in (childValue.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (long.TryParse(token, out var id) && id > 0 && id != session.UserId) ids.Add(id);
        }
        else if (childValue.ValueKind == JsonValueKind.Array)
            foreach (var token in childValue.EnumerateArray())
                if (long.TryParse(token.ToString(), out var id) && id > 0 && id != session.UserId) ids.Add(id);

        foreach (var id in ids.Order())
        {
            var name = "Student " + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                using var profile = await _api.GetUserAsync(session, id);
                var data = profile.RootElement.TryGetProperty("user", out var nested) ? nested : profile.RootElement;
                name = ProfileValue(data, "name_display", "display_name", "name") ?? name;
            }
            catch (Exception ex) { App.WriteStartupLog($"Could not resolve linked student {id}; retaining its child_uids entry.", ex); }
            _parentChildren.Add(new ParentLinkedStudent(id, name));
        }
        _parentChildren.Sort((a,b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name,b.Name));
    }

    private NavigationViewItem BuildParentStudentNavigationItem()
    {
        var root = new NavigationViewItem { Content = "Switch Student", Tag = "switch-student",
            Icon = new SymbolIcon(Symbol.People), SelectsOnInvoked = false, IsExpanded = true };
        root.MenuItems.Add(new NavigationViewItem { Content = "Parent account", Tag = "student:parent",
            Icon = new SymbolIcon(Symbol.Contact) });
        foreach (var student in _parentChildren)
        {
            var child = new NavigationViewItem { Content = student.Name,
                Tag = "student:" + student.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Icon = new SymbolIcon(Symbol.Contact) };
            AutomationProperties.SetName(child, "Switch learning context to " + student.Name);
            root.MenuItems.Add(child);
        }
        return root;
    }

    private async Task SelectLearningStudentAsync(long studentId)
    {
        if (_session is null || (studentId != 0 && !_parentChildren.Any(child => child.Id == studentId))) return;
        _selectedLearningUserId = studentId == _session.UserId ? 0 : studentId;
        _selectedLearningUserName = _parentChildren.FirstOrDefault(child => child.Id == _selectedLearningUserId)?.Name ?? "";
        _courses.Clear(); _groups.Clear();
        _selectedEntity = ""; _selectedEntityId = 0; _selectedEntityTab = "";
        _activeProfileUserId = 0;
        await LoadAccountCollectionsAsync();
        ShowHome();
    }
}
