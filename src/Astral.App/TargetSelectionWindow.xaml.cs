using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Astral.Core.Targets;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfPath = System.Windows.Shapes.Path;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace Astral.App;

public partial class TargetSelectionWindow : Window
{
    private readonly List<TargetCardState> _cards;

    public TargetSelectionWindow(
        TargetRegistry registry,
        TargetSelection currentSelection)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(currentSelection);
        InitializeComponent();
        _cards = registry.GetBuiltInTargets()
            .Select(target => new TargetCardState(
                target,
                currentSelection.SelectedTargetIds.Contains(
                    target.Id,
                    StringComparer.OrdinalIgnoreCase)))
            .ToList();
        CustomExecutableTextBox.Text = currentSelection.CustomExecutables.Count > 0
            ? currentSelection.CustomExecutables[0].Path
            : string.Empty;
        CustomDomainTextBox.Text = currentSelection.CustomDomains.Count > 0
            ? currentSelection.CustomDomains[0].Pattern
            : string.Empty;
        RenderCards();
        UpdateSelectedSummary();
    }

    public TargetSelection Selection { get; private set; } = TargetSelection.Default;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RenderCards();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ValidationMessage.Text = string.Empty;
        try
        {
            var selectedIds = _cards
                .Where(card => card.IsSelected)
                .Select(card => card.Target.Id)
                .ToArray();
            if (selectedIds.Length == 0
                && string.IsNullOrWhiteSpace(CustomExecutableTextBox.Text)
                && string.IsNullOrWhiteSpace(CustomDomainTextBox.Text))
            {
                ValidationMessage.Text = "En az bir hedef seçin.";
                return;
            }

            if (selectedIds.Contains(
                    TargetIds.CustomExecutable,
                    StringComparer.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(CustomExecutableTextBox.Text))
            {
                ValidationMessage.Text = "Özel EXE hedefi için geçerli bir .exe yolu girin.";
                return;
            }

            if (selectedIds.Contains(
                    TargetIds.CustomDomain,
                    StringComparer.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(CustomDomainTextBox.Text))
            {
                ValidationMessage.Text = "Özel Domain hedefi için geçerli bir domain girin.";
                return;
            }

            var executables = string.IsNullOrWhiteSpace(CustomExecutableTextBox.Text)
                ? []
                : new[] { CustomExecutableTarget.Create(CustomExecutableTextBox.Text) };
            var domains = string.IsNullOrWhiteSpace(CustomDomainTextBox.Text)
                ? []
                : new[] { CustomDomainTarget.Create(CustomDomainTextBox.Text) };

            Selection = new TargetSelection(selectedIds, executables, domains);
            DialogResult = true;
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or IOException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            ValidationMessage.Text = exception.Message;
        }
    }

    private void RenderCards()
    {
        TargetCardsPanel.Children.Clear();
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var filtered = _cards.Where(card =>
                string.IsNullOrWhiteSpace(query)
                || MatchesQuery(card.Target, query))
            .ToArray();

        if (filtered.Length == 0)
        {
            TargetCardsPanel.Children.Add(CreateEmptyState());
            return;
        }

        foreach (var card in filtered)
        {
            var checkbox = new WpfCheckBox
            {
                IsChecked = card.IsSelected,
                Margin = new Thickness(0, 0, 12, 12),
                Width = 244,
                MinHeight = 104,
                Padding = new Thickness(14),
                ToolTip = card.Target.Metadata.TryGetValue("note", out var note)
                    ? note
                    : card.Target.Label,
                Content = CreateCardContent(card.Target)
            };
            AutomationProperties.SetName(checkbox, $"{card.Target.Label} hedef kartı");
            checkbox.Checked += (_, _) =>
            {
                card.IsSelected = true;
                UpdateSelectedSummary();
            };
            checkbox.Unchecked += (_, _) =>
            {
                card.IsSelected = false;
                UpdateSelectedSummary();
            };
            TargetCardsPanel.Children.Add(checkbox);
        }
    }

    private static Border CreateEmptyState()
    {
        return new Border
        {
            MinWidth = 360,
            MinHeight = 92,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(82, 142, 200, 255)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(MediaColor.FromArgb(54, 16, 29, 49)),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Sonuç bulunamadı",
                        FontSize = 16,
                        FontWeight = FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "Aramayı sadeleştirip tekrar deneyin.",
                        Margin = new Thickness(0, 4, 0, 0),
                        FontSize = 12,
                        Foreground = new SolidColorBrush(MediaColor.FromRgb(184, 199, 215))
                    }
                }
            }
        };
    }

    private static Grid CreateCardContent(TargetDefinition target)
    {
        var icon = GetIconVisual(target.IconKey);
        var content = new Grid { MinHeight = 74 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconBadge = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(14),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(118, 245, 247, 251)),
            BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(icon.StartColor, icon.EndColor, 42),
            VerticalAlignment = VerticalAlignment.Center,
            Child = CreateIconMark(icon)
        };
        iconBadge.Effect = new DropShadowEffect
        {
            BlurRadius = 22,
            Direction = 270,
            Opacity = 0.42,
            ShadowDepth = 0,
            Color = icon.EndColor
        };
        content.Children.Add(iconBadge);

        var textPanel = new StackPanel
        {
            Margin = new Thickness(14, 0, 28, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(textPanel, 1);
        textPanel.Children.Add(new TextBlock
        {
            Text = target.Label,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var metaRow = new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 0),
            Orientation = System.Windows.Controls.Orientation.Horizontal
        };
        metaRow.Children.Add(new Border
        {
            Padding = new Thickness(8, 3, 8, 4),
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(92, 125, 235, 255)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(MediaColor.FromArgb(46, 125, 235, 255)),
            Child = new TextBlock
            {
                Text = target.ScopeLabel,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(142, 200, 255))
            }
        });
        textPanel.Children.Add(metaRow);
        textPanel.Children.Add(new TextBlock
        {
            Text = GetStatusLabel(target),
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(184, 199, 215))
        });

        content.Children.Add(textPanel);
        return content;
    }

    private static bool MatchesQuery(TargetDefinition target, string query)
    {
        return target.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || target.IconKey.Contains(query, StringComparison.OrdinalIgnoreCase)
            || target.Domains.Any(domain =>
                domain.Pattern.Contains(query, StringComparison.OrdinalIgnoreCase))
            || target.ExecutableHints.Any(executable =>
                executable.FileName.Contains(query, StringComparison.OrdinalIgnoreCase))
            || GetSearchAliases(target.Id).Any(alias =>
                alias.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetStatusLabel(TargetDefinition target)
    {
        return target.ScopeKind is TargetScopeKind.CustomExecutable or TargetScopeKind.CustomDomain
            ? "Bilgi gerekli"
            : "Kapsam hazır";
    }

    private static IReadOnlyList<string> GetSearchAliases(string targetId)
    {
        return targetId switch
        {
            TargetIds.Discord => ["dc", "voice", "chat"],
            TargetIds.Roblox => ["rbx", "oyun"],
            TargetIds.Wattpad => ["kitap", "okuma", "yazı"],
            TargetIds.BigoLive => ["bigo", "live"],
            TargetIds.Blogspot => ["blog", "blogger"],
            TargetIds.CustomExecutable => ["exe", "uygulama", "program"],
            TargetIds.CustomDomain => ["domain", "web", "site"],
            _ => []
        };
    }

    private static IconVisual GetIconVisual(string iconKey)
    {
        return iconKey switch
        {
            "discord" => new("D", IconMarkKind.Discord, Rgb(88, 101, 242), Rgb(125, 235, 255)),
            "roblox" => new("R", IconMarkKind.Roblox, Rgb(26, 31, 43), Rgb(196, 210, 230)),
            "wattpad" => new("W", IconMarkKind.Wattpad, Rgb(255, 103, 26), Rgb(255, 175, 72)),
            "bigo-live" => new("B", IconMarkKind.Live, Rgb(27, 169, 255), Rgb(93, 255, 146)),
            "azar" => new("A", IconMarkKind.Spark, Rgb(255, 89, 139), Rgb(125, 235, 255)),
            "tango" => new("T", IconMarkKind.Chat, Rgb(255, 124, 50), Rgb(255, 217, 85)),
            "livu" => new("L", IconMarkKind.Heart, Rgb(165, 91, 255), Rgb(93, 255, 146)),
            "imvu" => new("I", IconMarkKind.Cube, Rgb(35, 215, 162), Rgb(125, 235, 255)),
            "blogspot" => new("B", IconMarkKind.Blog, Rgb(245, 132, 31), Rgb(255, 199, 89)),
            "custom-exe" => new("EXE", IconMarkKind.Terminal, Rgb(75, 93, 126), Rgb(125, 235, 255)),
            "custom-domain" => new("WEB", IconMarkKind.Globe, Rgb(54, 72, 110), Rgb(93, 255, 146)),
            _ => new("A", IconMarkKind.Letter, Rgb(125, 235, 255), Rgb(93, 255, 146))
        };
    }

    private static FrameworkElement CreateIconMark(IconVisual icon)
    {
        var foreground = new SolidColorBrush(icon.ForegroundColor);
        return icon.Kind switch
        {
            IconMarkKind.Discord => CreateDiscordMark(foreground),
            IconMarkKind.Roblox => CreateRobloxMark(foreground, icon.StartColor),
            IconMarkKind.Live => CreateLiveMark(foreground),
            IconMarkKind.Spark => CreateSparkMark(foreground),
            IconMarkKind.Chat => CreateChatMark(foreground, icon.Mark),
            IconMarkKind.Heart => CreatePathMark(
                foreground,
                "M16,27 C8,21 4,16 5,10 C6,5 12,4 16,9 C20,4 26,5 27,10 C28,16 24,21 16,27 Z"),
            IconMarkKind.Cube => CreateCubeMark(foreground),
            IconMarkKind.Blog => CreateBlogMark(foreground),
            IconMarkKind.Terminal => CreateLetterMark(">_", 16, foreground),
            IconMarkKind.Globe => CreateGlobeMark(foreground),
            _ => CreateLetterMark(icon.Mark, icon.Mark.Length > 1 ? 13 : 23, foreground)
        };
    }

    private static Canvas CreateIconCanvas()
    {
        return new Canvas
        {
            Width = 32,
            Height = 32,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
    }

    private static Canvas CreateDiscordMark(WpfBrush foreground)
    {
        var canvas = CreateIconCanvas();
        canvas.Children.Add(new WpfPath
        {
            Data = Geometry.Parse("M7,14 C7,9 25,9 25,14 L27,22 C24,25 8,25 5,22 Z"),
            Stroke = foreground,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = WpfBrushes.Transparent
        });
        AddEllipse(canvas, 11, 16, 4, foreground);
        AddEllipse(canvas, 19, 16, 4, foreground);
        canvas.Children.Add(new WpfPath
        {
            Data = Geometry.Parse("M12,22 C14,23.5 18,23.5 20,22"),
            Stroke = foreground,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        return canvas;
    }

    private static Canvas CreateRobloxMark(WpfBrush foreground, MediaColor cutoutColor)
    {
        var canvas = CreateIconCanvas();
        var diamond = new WpfRectangle
        {
            Width = 21,
            Height = 21,
            Fill = foreground,
            RadiusX = 2,
            RadiusY = 2,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = new RotateTransform(45)
        };
        Canvas.SetLeft(diamond, 5.5);
        Canvas.SetTop(diamond, 5.5);
        canvas.Children.Add(diamond);

        var cutout = new WpfRectangle
        {
            Width = 7,
            Height = 7,
            Fill = new SolidColorBrush(cutoutColor),
            RadiusX = 1,
            RadiusY = 1,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = new RotateTransform(45)
        };
        Canvas.SetLeft(cutout, 12.5);
        Canvas.SetTop(cutout, 12.5);
        canvas.Children.Add(cutout);
        return canvas;
    }

    private static Canvas CreateLiveMark(WpfBrush foreground)
    {
        var canvas = CreateIconCanvas();
        AddEllipse(canvas, 4, 5, 22, WpfBrushes.Transparent, foreground, 2);
        canvas.Children.Add(new WpfPath
        {
            Data = Geometry.Parse("M14,11 L23,16 L14,21 Z"),
            Fill = foreground,
            StrokeLineJoin = PenLineJoin.Round
        });
        AddEllipse(canvas, 24, 5, 4, foreground);
        return canvas;
    }

    private static Canvas CreateSparkMark(WpfBrush foreground)
    {
        return CreatePathMark(
            foreground,
            "M16,3 L19.5,12.5 L29,16 L19.5,19.5 L16,29 L12.5,19.5 L3,16 L12.5,12.5 Z");
    }

    private static Canvas CreateChatMark(WpfBrush foreground, string mark)
    {
        var canvas = CreateIconCanvas();
        canvas.Children.Add(new WpfPath
        {
            Data = Geometry.Parse("M6,8 H26 V21 H16 L10,26 V21 H6 Z"),
            Stroke = foreground,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = WpfBrushes.Transparent
        });
        var letter = CreateLetterMark(mark, 15, foreground);
        Canvas.SetLeft(letter, 11);
        Canvas.SetTop(letter, 7);
        canvas.Children.Add(letter);
        return canvas;
    }

    private static Canvas CreateCubeMark(WpfBrush foreground)
    {
        var canvas = CreateIconCanvas();
        canvas.Children.Add(new WpfPath
        {
            Data = Geometry.Parse("M16,4 L27,10 L27,22 L16,28 L5,22 L5,10 Z M16,4 L16,16 M5,10 L16,16 L27,10 M16,16 L16,28"),
            Stroke = foreground,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = WpfBrushes.Transparent
        });
        return canvas;
    }

    private static Canvas CreateBlogMark(WpfBrush foreground)
    {
        var canvas = CreateIconCanvas();
        var body = new WpfRectangle
        {
            Width = 22,
            Height = 22,
            RadiusX = 6,
            RadiusY = 6,
            Stroke = foreground,
            StrokeThickness = 2,
            Fill = WpfBrushes.Transparent
        };
        Canvas.SetLeft(body, 5);
        Canvas.SetTop(body, 5);
        canvas.Children.Add(body);
        AddRoundedRect(canvas, 11, 11, 10, 4, foreground);
        AddRoundedRect(canvas, 11, 18, 13, 4, foreground);
        return canvas;
    }

    private static Canvas CreateGlobeMark(WpfBrush foreground)
    {
        var canvas = CreateIconCanvas();
        AddEllipse(canvas, 5, 5, 22, WpfBrushes.Transparent, foreground, 2);
        canvas.Children.Add(new WpfPath
        {
            Data = Geometry.Parse("M16,5 C11,10 11,22 16,27 M16,5 C21,10 21,22 16,27 M6,16 H26 M8,10 H24 M8,22 H24"),
            Stroke = foreground,
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        return canvas;
    }

    private static Canvas CreatePathMark(WpfBrush foreground, string pathData)
    {
        var canvas = CreateIconCanvas();
        canvas.Children.Add(new WpfPath
        {
            Data = Geometry.Parse(pathData),
            Fill = foreground,
            StrokeLineJoin = PenLineJoin.Round
        });
        return canvas;
    }

    private static TextBlock CreateLetterMark(string mark, double fontSize, WpfBrush foreground)
    {
        return new TextBlock
        {
            Text = mark,
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Foreground = foreground,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
    }

    private static void AddEllipse(
        Canvas canvas,
        double left,
        double top,
        double size,
        WpfBrush fill,
        WpfBrush? stroke = null,
        double strokeThickness = 0)
    {
        var ellipse = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = strokeThickness
        };
        Canvas.SetLeft(ellipse, left);
        Canvas.SetTop(ellipse, top);
        canvas.Children.Add(ellipse);
    }

    private static void AddRoundedRect(
        Canvas canvas,
        double left,
        double top,
        double width,
        double height,
        WpfBrush fill)
    {
        var rect = new WpfRectangle
        {
            Width = width,
            Height = height,
            RadiusX = 2,
            RadiusY = 2,
            Fill = fill
        };
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        canvas.Children.Add(rect);
    }

    private static MediaColor Rgb(byte red, byte green, byte blue) =>
        MediaColor.FromRgb(red, green, blue);

    private sealed record IconVisual(
        string Mark,
        IconMarkKind Kind,
        MediaColor StartColor,
        MediaColor EndColor,
        MediaColor? Foreground = null)
    {
        public MediaColor ForegroundColor => Foreground ?? MediaColor.FromRgb(245, 247, 251);
    }

    private enum IconMarkKind
    {
        Letter,
        Discord,
        Roblox,
        Wattpad,
        Live,
        Spark,
        Chat,
        Heart,
        Cube,
        Blog,
        Terminal,
        Globe
    }

    private void UpdateSelectedSummary()
    {
        var labels = _cards
            .Where(card => card.IsSelected)
            .Select(card => card.Target.Label)
            .ToArray();
        SelectedSummary.Text = labels.Length == 0
            ? "Seçili hedef yok"
            : "Seçilenler: " + string.Join(", ", labels);
    }

    private sealed class TargetCardState(
        TargetDefinition target,
        bool isSelected)
    {
        public TargetDefinition Target { get; } = target;

        public bool IsSelected { get; set; } = isSelected;
    }
}
