using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClientAvalonia.Core;
using Rampastring.Tools;

namespace ClientAvalonia.Views;

/// <summary>
/// Startup gate / Settings-style workspace picker (design §5 / §7).
/// Layout: header / scrollable list / fixed footer (buttons never covered by the list).
/// </summary>
public sealed class WorkspacePickerWindow : Window
{
    private readonly WorkspacePickerController _controller = new();
    private readonly ListBox _list;
    private readonly TextBox _modNameBox;
    private readonly ComboBox _clientGameTypeBox;
    private readonly TextBlock _status;
    private bool _syncingListSelection;

    public WorkspacePickerWindow()
    {
        Title = "选择 Mod 工作区";
        Width = 760;
        Height = 560;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = true;
        Background = new SolidColorBrush(Color.Parse("#1A1612"));

        _list = new ListBox
        {
            ItemsSource = _controller.Entries,
            Background = new SolidColorBrush(Color.Parse("#241E18")),
            Foreground = Brushes.WhiteSmoke,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            // Non-virtualizing panel: variable-height rows + collection merge corrupt virtualized layout (Bug1).
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel()),
        };

        _list.ItemTemplate = new FuncDataTemplate<ModRegistryEntry>((entry, _) =>
        {
            if (entry == null)
                return new Border { Height = 1 };

            var panel = new StackPanel
            {
                Spacing = 2,
                Margin = new Thickness(4, 6),
                MinHeight = 44,
            };
            panel.Children.Add(new TextBlock
            {
                Text = $"{entry.DisplayName}  [{entry.ModName}]",
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.WhiteSmoke,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"{entry.StatusLabel} · {entry.SourceLabel} · {entry.InstallPath ?? "(无路径)"}",
                FontSize = 11,
                Opacity = 0.75,
                Foreground = Brushes.WhiteSmoke,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 48,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            return panel;
        });

        _modNameBox = new TextBox
        {
            Watermark = "注册用 ModName（如 MomentOfGenesis）",
            MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _clientGameTypeBox = new ComboBox
        {
            ItemsSource = ModWorkspaceRegistry.ClientGameTypeOptions,
            SelectedIndex = 1, // YR
            MinWidth = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.9,
            Foreground = new SolidColorBrush(Color.Parse("#E8C89A")),
            Margin = new Thickness(0, 4, 0, 0),
            MaxHeight = 72,
        };

        var launch = CreateButton("启动所选工作区", (_, _) => SafeUi(OnLaunch));
        var register = CreateButton("注册到 Avalonia 表", (_, _) => _ = SafeUiAsync(OnRegisterAsync));
        var clear = CreateButton("清除 Avalonia 项", (_, _) => SafeUi(OnClear));
        var cleanupDx = CreateButton("清理无用 DX 脏键", (_, _) => SafeUi(OnCleanupDx));
        var browse = CreateButton("浏览文件夹…", (_, _) => _ = SafeUiAsync(OnBrowseAsync));
        var probe = CreateButton("探测本机旁路", (_, _) => SafeUi(OnProbe));
        var refresh = CreateButton("刷新", (_, _) => SafeUi(() => SyncFromController(() => _controller.Refresh())));
        var exit = CreateButton("退出", (_, _) => SafeUi(OnExit));

        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { launch, register, clear, cleanupDx, browse, probe, refresh, exit },
        };

        var header = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                new TextBlock
                {
                    Text = "ClientAvalonia · Mod 工作区",
                    FontSize = 22,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#FF8C32")),
                },
                new TextBlock
                {
                    Text = "索引键：HKCU\\SOFTWARE\\ClientAvalonia\\ModWorkspaces\\{ModName}\\InstallPath（与 DX 启动器键隔离）\n"
                         + "「注册到 Avalonia 表」：无可用路径时会弹出目录选择；有选中项则直接写入。\n"
                         + "ClientGameType 手动选择（TS/YR/Ares/RA）；只写 Avalonia 表 / 会话，不改 ClientDefinitions.ini。",
                    FontSize = 12,
                    Opacity = 0.8,
                    Foreground = Brushes.WhiteSmoke,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };

        var footer = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "ModName",
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = Brushes.WhiteSmoke,
                            Margin = new Thickness(0, 0, 8, 0),
                            [Grid.ColumnProperty] = 0,
                        },
                        new Border
                        {
                            Child = _modNameBox,
                            [Grid.ColumnProperty] = 1,
                        },
                        new TextBlock
                        {
                            Text = "ClientGameType",
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = Brushes.WhiteSmoke,
                            Margin = new Thickness(12, 0, 8, 0),
                            [Grid.ColumnProperty] = 2,
                        },
                        new Border
                        {
                            Child = _clientGameTypeBox,
                            [Grid.ColumnProperty] = 3,
                        },
                    },
                },
                buttons,
                _status,
            },
        };

        var root = new Grid
        {
            Margin = new Thickness(20),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                header,
                new Border
                {
                    Child = _list,
                    BorderBrush = new SolidColorBrush(Color.Parse("#55FF8C32")),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(2),
                    ClipToBounds = true,
                    [Grid.RowProperty] = 1,
                },
                footer,
            },
        };
        Grid.SetRow(header, 0);
        Grid.SetRow(footer, 2);

        Content = root;

        Opened += (_, _) => SafeUi(() => SyncFromController(() => _controller.Refresh()));
        Closing += OnClosing;
        _list.SelectionChanged += (_, _) =>
        {
            if (_syncingListSelection)
                return;

            if (_list.SelectedItem is ModRegistryEntry entry)
            {
                _controller.Selected = entry;
                _controller.ApplySelectionFields(entry);
                _modNameBox.Text = _controller.ModNameText;
                SyncClientGameTypeCombo();
            }
        };
        _modNameBox.LostFocus += (_, _) => _controller.ModNameText = _modNameBox.Text ?? string.Empty;
        _clientGameTypeBox.SelectionChanged += (_, _) =>
        {
            if (_clientGameTypeBox.SelectedItem is string type)
                _controller.ClientGameTypeText = type;
        };
    }

    public event Action? WorkspaceBound;

    private static Button CreateButton(string text, EventHandler<Avalonia.Interactivity.RoutedEventArgs> handler)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(12, 6),
            MinWidth = 100,
        };
        button.Click += handler;
        return button;
    }

    private void SafeUi(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Logger.Log($"WorkspacePickerWindow: UI action failed: {ex}");
            _status.Text = "操作失败：" + ex.Message;
        }
    }

    private async Task SafeUiAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Logger.Log($"WorkspacePickerWindow: UI async action failed: {ex}");
            _status.Text = "操作失败：" + ex.Message;
        }
    }

    private void SyncFromController(Action action)
    {
        action();
        _status.Text = _controller.StatusText;
        _modNameBox.Text = _controller.ModNameText;
        SyncClientGameTypeCombo();
        ScheduleListSelectionSync();
    }

    private void ScheduleListSelectionSync()
    {
        Dispatcher.UIThread.Post(SyncListSelectionFromController, DispatcherPriority.Loaded);
    }

    private void SyncListSelectionFromController()
    {
        ModRegistryEntry? selected = _controller.Selected;
        if (selected == null)
        {
            if (_list.SelectedIndex >= 0)
            {
                _syncingListSelection = true;
                try
                {
                    _list.SelectedIndex = -1;
                }
                finally
                {
                    _syncingListSelection = false;
                }
            }

            return;
        }

        int index = FindListIndex(selected);
        if (index < 0)
            return;

        if (_list.SelectedIndex == index)
            return;

        _syncingListSelection = true;
        try
        {
            _list.SelectedIndex = index;
        }
        finally
        {
            _syncingListSelection = false;
        }
    }

    private int FindListIndex(ModRegistryEntry selected)
    {
        string? path = selected.InstallPath?.TrimEnd('\\', '/');
        for (int i = 0; i < _controller.Entries.Count; i++)
        {
            ModRegistryEntry e = _controller.Entries[i];
            if (!string.IsNullOrWhiteSpace(path)
                && string.Equals(
                    e.InstallPath?.TrimEnd('\\', '/'),
                    path,
                    StringComparison.OrdinalIgnoreCase)
                && e.ModName.Equals(selected.ModName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            for (int i = 0; i < _controller.Entries.Count; i++)
            {
                if (string.Equals(
                        _controller.Entries[i].InstallPath?.TrimEnd('\\', '/'),
                        path,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return _controller.Entries.IndexOf(selected);
    }

    private void SyncClientGameTypeCombo()
    {
        string type = ModWorkspaceRegistry.IsKnownClientGameType(_controller.ClientGameTypeText)
            ? _controller.ClientGameTypeText
            : "YR";
        _controller.ClientGameTypeText = type;

        int idx = Array.FindIndex(
            ModWorkspaceRegistry.ClientGameTypeOptions,
            o => o.Equals(type, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && _clientGameTypeBox.SelectedIndex != idx)
            _clientGameTypeBox.SelectedIndex = idx;
    }

    private void PushFormToController()
    {
        _controller.ModNameText = _modNameBox.Text ?? string.Empty;
        if (_clientGameTypeBox.SelectedItem is string type
            && ModWorkspaceRegistry.IsKnownClientGameType(type))
        {
            _controller.ClientGameTypeText = type;
        }
        else if (!ModWorkspaceRegistry.IsKnownClientGameType(_controller.ClientGameTypeText))
        {
            _controller.ClientGameTypeText = "YR";
        }
    }

    private async Task<string?> PickGameRootFolderAsync()
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "选择游戏根目录（含 Resources\\ClientDefinitions.ini）",
                AllowMultiple = false,
            }).ConfigureAwait(true);

        if (folders.Count == 0)
            return null;

        return folders[0].TryGetLocalPath();
    }

    private async Task OnBrowseAsync()
    {
        string? path = await PickGameRootFolderAsync().ConfigureAwait(true);
        if (path == null)
        {
            _status.Text = "已取消选择目录。";
            return;
        }

        PushFormToController();
        SyncFromController(() => _controller.TryAddFolder(path));
    }

    private async Task OnRegisterAsync()
    {
        PushFormToController();
        WorkspacePickerCommandResult result = _controller.BeginRegister();
        _status.Text = result.StatusText;

        if (result.UiRequest == WorkspacePickerUiRequest.BrowseFolderForRegister)
        {
            string? path = await PickGameRootFolderAsync().ConfigureAwait(true);
            if (path == null)
            {
                _status.Text = "已取消选择目录。";
                return;
            }

            PushFormToController();
            SyncFromController(() =>
            {
                WorkspacePickerCommandResult completed = _controller.CompleteRegisterFromFolder(path);
                _status.Text = completed.StatusText;
            });
            return;
        }

        SyncFromController(() => { });
        _status.Text = result.StatusText;
    }

    private void OnProbe()
    {
        PushFormToController();
        SyncFromController(() => _controller.TryProbeLocal());
    }

    private void OnClear()
    {
        PushFormToController();
        SyncFromController(() => _controller.TryClearSelected());
    }

    private void OnCleanupDx()
        => SyncFromController(() => _controller.TryCleanupOrphanDx());

    private void OnLaunch()
    {
        PushFormToController();
        if (!_controller.TryLaunchSelected())
        {
            _status.Text = _controller.StatusText;
            return;
        }

        _status.Text = _controller.StatusText;
        WorkspaceBound?.Invoke();
    }

    private void OnExit()
    {
        _controller.MarkUserRequestedExit();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Close();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_controller.ShouldCancelClose(ModWorkspaceBinder.IsBound))
            return;

        e.Cancel = true;
        _status.Text = "请选择工作区并「启动」，或点「退出」结束。";
    }
}
