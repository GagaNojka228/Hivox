using Microsoft.Maui.Controls.Shapes;

namespace Hivox.Mobile.Services;

/// <summary>
/// Custom Hivox-styled modal dialogs (Alert, Confirm, Prompt, ActionSheet) matching the app's Material 3 theme.
/// </summary>
public static class HivoxDialog
{
    public static Task ShowAlertAsync(string title, string message, string ok = "OK")
        => ShowOverlayAsync<bool>(tcs => BuildAlertCard(title, message, ok, null, tcs));

    public static Task<bool> ShowConfirmAsync(string title, string message, string accept = "Да", string cancel = "Отмена")
        => ShowOverlayAsync<bool>(tcs => BuildAlertCard(title, message, accept, cancel, tcs));

    public static Task<string?> ShowPromptAsync(
        string title,
        string message,
        string accept = "OK",
        string cancel = "Отмена",
        string? placeholder = null,
        string? initialValue = null,
        Keyboard? keyboard = null)
        => ShowOverlayAsync<string?>(tcs => BuildPromptCard(title, message, accept, cancel, placeholder, initialValue, keyboard, tcs));

    public static Task<string?> ShowActionSheetAsync(
        string title,
        string? cancel,
        string? destruction,
        params string[] buttons)
        => ShowOverlayAsync<string?>(tcs => BuildActionSheetCard(title, cancel, destruction, buttons, tcs));

    public static Task<string?> ShowIconMenuAsync(
        string? title,
        params (string Icon, string Text, bool IsDestructive)[] items)
        => ShowOverlayAsync<string?>(tcs => BuildIconMenuCard(title, items, tcs));

    private static readonly SemaphoreSlim _modalGate = new(1, 1);

    private static async Task<T> ShowOverlayAsync<T>(Func<TaskCompletionSource<T>, (View Card, Action FocusAction)> buildCard)
    {
        await _modalGate.WaitAsync();
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var nav = Shell.Current?.Navigation ?? Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
                if (nav == null)
                    return default!;

                var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                var (card, focusAction) = buildCard(tcs);

                var overlay = new Grid
                {
                    BackgroundColor = Color.FromArgb("#A6070410"),
                    Padding = new Thickness(24, 32),
                    Opacity = 0
                };

                var backdropTap = new TapGestureRecognizer();
                backdropTap.Tapped += (_, _) => tcs.TrySetResult(default!);
                overlay.GestureRecognizers.Add(backdropTap);

                // Prevent taps inside the card from dismissing the backdrop
                card.GestureRecognizers.Add(new TapGestureRecognizer());
                card.Scale = 0.94;

                overlay.Children.Add(card);

                var page = new DialogModalPage(overlay, () => tcs.TrySetResult(default!));

                await nav.PushModalAsync(page, animated: false);

                _ = Task.WhenAll(
                    overlay.FadeToAsync(1, 130, Easing.CubicOut),
                    card.ScaleToAsync(1.0, 140, Easing.CubicOut));

                focusAction();

                T result;
                try
                {
                    result = await tcs.Task;
                }
                finally
                {
                    overlay.InputTransparent = true;
                    overlay.IsVisible = false;
                    overlay.AbortAnimation("FadeTo");
                    card.AbortAnimation("ScaleTo");
                    try
                    {
                        if (nav.ModalStack.Count > 0 && nav.ModalStack[^1] == page)
                            await nav.PopModalAsync(animated: false);
                        else if (nav.ModalStack.Contains(page))
                            await nav.PopModalAsync(animated: false);
                    }
                    catch { }
                }

                // Let Android FragmentManager finish committing modal dismissal before callers
                // navigate via Shell.GoToAsync or open another modal dialog.
                await Task.Delay(50);
                return result;
            });
        }
        finally
        {
            _modalGate.Release();
        }
    }

    private static (View Card, Action FocusAction) BuildAlertCard(
        string title,
        string message,
        string accept,
        string? cancel,
        TaskCompletionSource<bool> tcs)
    {
        var card = CreateCardContainer();
        var stack = new VerticalStackLayout { Spacing = 14 };

        if (!string.IsNullOrWhiteSpace(title))
            stack.Children.Add(CreateTitleLabel(title));

        if (!string.IsNullOrWhiteSpace(message))
            stack.Children.Add(CreateMessageLabel(message));

        var btnGrid = new Grid
        {
            ColumnSpacing = 10,
            Margin = new Thickness(0, 6, 0, 0)
        };

        if (!string.IsNullOrEmpty(cancel))
        {
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            var cancelBtn = CreateSecondaryButton(cancel);
            cancelBtn.Clicked += (_, _) => tcs.TrySetResult(false);
            Grid.SetColumn(cancelBtn, 0);
            btnGrid.Children.Add(cancelBtn);

            var okBtn = CreatePrimaryButton(accept);
            okBtn.Clicked += (_, _) => tcs.TrySetResult(true);
            Grid.SetColumn(okBtn, 1);
            btnGrid.Children.Add(okBtn);
        }
        else
        {
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var okBtn = CreatePrimaryButton(accept);
            okBtn.Clicked += (_, _) => tcs.TrySetResult(true);
            btnGrid.Children.Add(okBtn);
        }

        stack.Children.Add(btnGrid);
        card.Content = stack;
        return (card, () => { });
    }

    private static (View Card, Action FocusAction) BuildPromptCard(
        string title,
        string message,
        string accept,
        string cancel,
        string? placeholder,
        string? initialValue,
        Keyboard? keyboard,
        TaskCompletionSource<string?> tcs)
    {
        var card = CreateCardContainer();
        var stack = new VerticalStackLayout { Spacing = 12 };

        if (!string.IsNullOrWhiteSpace(title))
            stack.Children.Add(CreateTitleLabel(title));

        if (!string.IsNullOrWhiteSpace(message))
            stack.Children.Add(CreateMessageLabel(message));

        var entry = new Entry
        {
            Text = initialValue ?? "",
            Placeholder = placeholder ?? "",
            Keyboard = keyboard ?? Keyboard.Default,
            FontSize = 14.5,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center,
            ReturnType = ReturnType.Done
        };
        entry.SetDynamicResource(Entry.TextColorProperty, "M3OnSurface");
        entry.SetDynamicResource(Entry.PlaceholderColorProperty, "M3Outline");
        entry.Completed += (_, _) => tcs.TrySetResult(entry.Text);

        var inputBorder = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            StrokeThickness = 1,
            HeightRequest = 46,
            Padding = new Thickness(12, 0),
            Content = entry
        };
        inputBorder.SetDynamicResource(Border.BackgroundColorProperty, "M3SurfaceContainerHigh");
        inputBorder.SetDynamicResource(Border.StrokeProperty, "M3OutlineVariantBrush");
        stack.Children.Add(inputBorder);

        var btnGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var cancelBtn = CreateSecondaryButton(cancel);
        cancelBtn.Clicked += (_, _) => tcs.TrySetResult(null);
        Grid.SetColumn(cancelBtn, 0);
        btnGrid.Children.Add(cancelBtn);

        var okBtn = CreatePrimaryButton(accept);
        okBtn.Clicked += (_, _) => tcs.TrySetResult(entry.Text ?? "");
        Grid.SetColumn(okBtn, 1);
        btnGrid.Children.Add(okBtn);

        stack.Children.Add(btnGrid);
        card.Content = stack;

        return (card, () =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(120);
                if (!tcs.Task.IsCompleted)
                    entry.Focus();
            });
        });
    }

    private static (View Card, Action FocusAction) BuildActionSheetCard(
        string title,
        string? cancel,
        string? destruction,
        string[] buttons,
        TaskCompletionSource<string?> tcs)
    {
        var card = CreateCardContainer();
        var root = new VerticalStackLayout { Spacing = 10 };

        if (!string.IsNullOrWhiteSpace(title))
        {
            var titleLbl = CreateTitleLabel(title);
            titleLbl.Margin = new Thickness(2, 0, 2, 4);
            root.Children.Add(titleLbl);
        }

        var listStack = new VerticalStackLayout { Spacing = 8 };
        foreach (string option in buttons ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(option)) continue;
            string captured = option;

            var row = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
                StrokeThickness = 1,
                Padding = new Thickness(14, 12),
                Content = CreateOptionRow(captured, isDestructive: false)
            };
            row.SetDynamicResource(Border.BackgroundColorProperty, "M3SurfaceContainerHigh");
            row.SetDynamicResource(Border.StrokeProperty, "M3OutlineVariantBrush");

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => tcs.TrySetResult(captured);
            row.GestureRecognizers.Add(tap);
            listStack.Children.Add(row);
        }

        if (!string.IsNullOrWhiteSpace(destruction))
        {
            string capturedDestruction = destruction;
            var destRow = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
                StrokeThickness = 1,
                BackgroundColor = Color.FromArgb("#22F87171"),
                Stroke = new SolidColorBrush(Color.FromArgb("#4DF87171")),
                Padding = new Thickness(14, 12),
                Content = CreateOptionRow(capturedDestruction, isDestructive: true)
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => tcs.TrySetResult(capturedDestruction);
            destRow.GestureRecognizers.Add(tap);
            listStack.Children.Add(destRow);
        }

        var scroll = new ScrollView
        {
            MaximumHeightRequest = 380,
            Content = listStack
        };
        root.Children.Add(scroll);

        if (!string.IsNullOrWhiteSpace(cancel))
        {
            var cancelBtn = CreateSecondaryButton(cancel);
            cancelBtn.Margin = new Thickness(0, 4, 0, 0);
            cancelBtn.Clicked += (_, _) => tcs.TrySetResult(cancel);
            root.Children.Add(cancelBtn);
        }

        card.Content = root;
        return (card, () => { });
    }

    private static (View Card, Action FocusAction) BuildIconMenuCard(
        string? title,
        (string Icon, string Text, bool IsDestructive)[] items,
        TaskCompletionSource<string?> tcs)
    {
        var card = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            StrokeThickness = 1,
            Padding = new Thickness(8),
            MaximumWidthRequest = 300,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 276
        };
        card.SetDynamicResource(Border.BackgroundColorProperty, "M3SurfaceContainer");
        card.SetDynamicResource(Border.StrokeProperty, "M3OutlineVariantBrush");

        var root = new VerticalStackLayout { Spacing = 2 };
        if (!string.IsNullOrWhiteSpace(title))
        {
            var hdr = new Label
            {
                Text = title,
                FontSize = 11.5,
                FontAttributes = FontAttributes.Bold,
                Margin = new Thickness(14, 8, 14, 4),
                LineBreakMode = LineBreakMode.TailTruncation
            };
            hdr.SetDynamicResource(Label.TextColorProperty, "M3OnSurfaceVariant");
            root.Children.Add(hdr);
        }

        bool dividerAdded = false;
        bool itemSelected = false;
        foreach (var (icon, text, isDestructive) in items ?? Array.Empty<(string, string, bool)>())
        {
            if (isDestructive && !dividerAdded)
            {
                dividerAdded = true;
                var div = new BoxView
                {
                    HeightRequest = 1,
                    Opacity = 0.45,
                    Margin = new Thickness(8, 4)
                };
                div.SetDynamicResource(BoxView.ColorProperty, "M3OutlineVariant");
                root.Children.Add(div);
            }

            string captured = text;
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition(new GridLength(28)),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 12,
                Padding = new Thickness(12, 12)
            };

            var iconLbl = new Label
            {
                Text = icon,
                FontFamily = "MaterialIcons",
                FontSize = 20,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            iconLbl.SetDynamicResource(Label.TextColorProperty, isDestructive ? "M3Red" : "M3OnSurfaceVariant");

            var textLbl = new Label
            {
                Text = text,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center
            };
            textLbl.SetDynamicResource(Label.TextColorProperty, isDestructive ? "M3Red" : "M3OnSurface");

            Grid.SetColumn(iconLbl, 0);
            Grid.SetColumn(textLbl, 1);
            grid.Children.Add(iconLbl);
            grid.Children.Add(textLbl);

            var row = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                Content = grid
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                if (itemSelected || tcs.Task.IsCompleted) return;
                itemSelected = true;
                try
                {
                    row.SetDynamicResource(Border.BackgroundColorProperty, isDestructive ? "M3RedContainer" : "M3SurfaceContainerHigh");
                    await row.ScaleToAsync(0.96, 65, Easing.CubicOut);
                }
                catch { }
                tcs.TrySetResult(captured);
            };
            row.GestureRecognizers.Add(tap);
            root.Children.Add(row);
        }

        card.Content = root;
        return (card, () => { });
    }

    private static View CreateOptionRow(string text, bool isDestructive)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };

        var lbl = new Label
        {
            Text = text,
            FontSize = 14.5,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        if (isDestructive)
            lbl.SetDynamicResource(Label.TextColorProperty, "M3Red");
        else
            lbl.SetDynamicResource(Label.TextColorProperty, "M3OnSurface");

        var chevron = new Label
        {
            Text = "\uE5CC",
            FontFamily = "MaterialIcons",
            FontSize = 18,
            VerticalOptions = LayoutOptions.Center
        };
        chevron.SetDynamicResource(Label.TextColorProperty, isDestructive ? "M3Red" : "M3Outline");

        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(chevron, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(chevron);
        return grid;
    }

    private static Border CreateCardContainer()
    {
        var card = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            StrokeThickness = 1,
            Padding = new Thickness(20),
            MaximumWidthRequest = 360,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center
        };
        card.SetDynamicResource(Border.BackgroundColorProperty, "M3SurfaceContainer");
        card.SetDynamicResource(Border.StrokeProperty, "M3OutlineVariantBrush");
        return card;
    }

    private static Label CreateTitleLabel(string title)
    {
        var lbl = new Label
        {
            Text = title,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold
        };
        lbl.SetDynamicResource(Label.TextColorProperty, "M3OnSurface");
        return lbl;
    }

    private static Label CreateMessageLabel(string message)
    {
        var lbl = new Label
        {
            Text = message,
            FontSize = 13.5,
            LineHeight = 1.25
        };
        lbl.SetDynamicResource(Label.TextColorProperty, "M3OnSurfaceVariant");
        return lbl;
    }

    private static Button CreatePrimaryButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            FontSize = 13.5,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 16,
            HeightRequest = 44
        };
        btn.SetDynamicResource(Button.BackgroundColorProperty, "M3Primary");
        btn.SetDynamicResource(Button.TextColorProperty, "M3OnPrimary");
        return btn;
    }

    private static Button CreateSecondaryButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            FontSize = 13.5,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 16,
            HeightRequest = 44,
            BorderWidth = 1
        };
        btn.SetDynamicResource(Button.BackgroundColorProperty, "M3SurfaceContainerHigh");
        btn.SetDynamicResource(Button.TextColorProperty, "M3OnSurface");
        btn.SetDynamicResource(Button.BorderColorProperty, "M3OutlineVariant");
        return btn;
    }

    private sealed class DialogModalPage : ContentPage
    {
        private readonly Action _onCancel;

        public DialogModalPage(View content, Action onCancel)
        {
            _onCancel = onCancel;
            BackgroundColor = Colors.Transparent;
            Shell.SetNavBarIsVisible(this, false);
            NavigationPage.SetHasNavigationBar(this, false);
            Content = content;
            Unloaded += (_, _) => _onCancel();
        }

        protected override bool OnBackButtonPressed()
        {
            _onCancel();
            return true;
        }
    }
}
