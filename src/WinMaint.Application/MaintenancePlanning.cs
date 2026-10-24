using WinMaint.Domain;

namespace WinMaint.Application;

public static class MaintenancePlanBuilder
{
    public const string SchemaVersion = "1.0";

    public static MaintenancePlan BuildPreview(IEnumerable<string> selectedActionIds)
    {
        ArgumentNullException.ThrowIfNull(selectedActionIds);
        var selected = new HashSet<string>(selectedActionIds, StringComparer.Ordinal);
        var catalog = MaintenanceCatalog.SafeActions.ToDictionary(action => action.Id, StringComparer.Ordinal);
        var unknown = selected.Where(id => !catalog.ContainsKey(id)).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
        {
            throw new ArgumentException($"Acciones desconocidas: {string.Join(", ", unknown)}", nameof(selectedActionIds));
        }

        AddDependencies(selected, catalog);
        var ordered = TopologicalOrder(selected, catalog);
        var items = ordered.Select(action => new MaintenancePlanItem(
            action,
            action.Preconditions.Select(precondition => new MaintenancePreconditionResult(
                precondition.Id,
                MaintenancePreconditionStatus.Unknown,
                "Pendiente de comprobar antes de ejecutar.")).ToArray())).ToArray();

        return new MaintenancePlan(SchemaVersion, DateTimeOffset.Now, MaintenanceProfile.Safe, items);
    }

    private static void AddDependencies(
        HashSet<string> selected,
        Dictionary<string, MaintenanceActionDescriptor> catalog)
    {
        var pending = new Stack<string>(selected);
        while (pending.TryPop(out var id))
        {
            foreach (var dependency in catalog[id].DependsOn)
            {
                if (!catalog.ContainsKey(dependency))
                {
                    throw new InvalidOperationException($"Dependencia no registrada: {dependency}");
                }

                if (selected.Add(dependency))
                {
                    pending.Push(dependency);
                }
            }
        }
    }

    private static List<MaintenanceActionDescriptor> TopologicalOrder(
        HashSet<string> selected,
        Dictionary<string, MaintenanceActionDescriptor> catalog)
    {
        var result = new List<MaintenanceActionDescriptor>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in MaintenanceCatalog.SafeActions.Where(action => selected.Contains(action.Id)))
        {
            Visit(action.Id, selected, catalog, visited, result);
        }

        return result;
    }

    private static void Visit(
        string id,
        HashSet<string> selected,
        Dictionary<string, MaintenanceActionDescriptor> catalog,
        HashSet<string> visited,
        List<MaintenanceActionDescriptor> result)
    {
        if (!visited.Add(id))
        {
            return;
        }

        foreach (var dependency in catalog[id].DependsOn.Where(selected.Contains))
        {
            Visit(dependency, selected, catalog, visited, result);
        }

        result.Add(catalog[id]);
    }
}
