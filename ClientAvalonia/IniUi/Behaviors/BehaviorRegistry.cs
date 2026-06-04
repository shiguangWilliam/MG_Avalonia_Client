using System.Windows.Input;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.IniUi.Behaviors;

public interface IUiBehavior
{
    void OnClick(UiNodeViewModel target);
}

/// <summary>Maps control Id → click handlers; layout stays in INI/tree, behavior lives here.</summary>
public sealed class BehaviorRegistry
{
    private readonly Dictionary<string, IUiBehavior> _behaviors = new(StringComparer.OrdinalIgnoreCase);
    private readonly IUiBehavior _noop = new NoopBehavior();

    public void Register(string controlId, IUiBehavior behavior) => _behaviors[controlId] = behavior;

    public void Register(string controlId, Action<UiNodeViewModel> handler)
        => _behaviors[controlId] = new DelegateBehavior(handler);

    /// <summary>Runs after any existing handler for the same control id.</summary>
    public void RegisterAfter(string controlId, Action<UiNodeViewModel> handler)
    {
        IUiBehavior existing = Resolve(controlId);
        _behaviors[controlId] = new ChainedBehavior(existing, new DelegateBehavior(handler));
    }

    public void Clear() => _behaviors.Clear();

    public ICommand CreateClickCommand(UiNodeViewModel vm)
        => new RelayCommand(() => Resolve(vm.Id).OnClick(vm));

    public IUiBehavior Resolve(string controlId)
        => _behaviors.TryGetValue(controlId, out IUiBehavior? behavior) ? behavior : _noop;

    private sealed class DelegateBehavior(Action<UiNodeViewModel> handler) : IUiBehavior
    {
        public void OnClick(UiNodeViewModel target) => handler(target);
    }

    private sealed class ChainedBehavior(IUiBehavior first, IUiBehavior second) : IUiBehavior
    {
        public void OnClick(UiNodeViewModel target)
        {
            first.OnClick(target);
            second.OnClick(target);
        }
    }

    private sealed class NoopBehavior : IUiBehavior
    {
        public void OnClick(UiNodeViewModel target) { }
    }

    private sealed class RelayCommand(Action execute) : ICommand
    {
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
