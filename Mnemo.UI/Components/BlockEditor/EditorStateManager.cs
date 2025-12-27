using System;

namespace Mnemo.UI.Components.BlockEditor;

public class EditorStateManager
{
    public enum State
    {
        Normal,
        ShowingSlashMenu,
        UpdatingFromViewModel
    }

    private State _currentState = State.Normal;
    private string _previousText = string.Empty;

    public State CurrentState
    {
        get => _currentState;
        private set => _currentState = value;
    }

    public string PreviousText
    {
        get => _previousText;
        set => _previousText = value ?? string.Empty;
    }

    public bool IsUpdatingFromViewModel => CurrentState == State.UpdatingFromViewModel;
    public bool IsSlashMenuVisible => CurrentState == State.ShowingSlashMenu;
    public bool IsNormal => CurrentState == State.Normal;

    public void SetState(State state)
    {
        CurrentState = state;
    }

    public void SetNormal() => SetState(State.Normal);
    public void SetShowingSlashMenu() => SetState(State.ShowingSlashMenu);
    public void SetUpdatingFromViewModel() => SetState(State.UpdatingFromViewModel);

    public IDisposable BeginUpdate()
    {
        SetUpdatingFromViewModel();
        return new StateRestorer(this);
    }

    private class StateRestorer : IDisposable
    {
        private readonly EditorStateManager _manager;

        public StateRestorer(EditorStateManager manager)
        {
            _manager = manager;
        }

        public void Dispose()
        {
            _manager.SetNormal();
        }
    }
}


