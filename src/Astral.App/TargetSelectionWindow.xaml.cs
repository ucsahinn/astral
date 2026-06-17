using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Astral.Core.Targets;
using MediaColor = System.Windows.Media.Color;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;

namespace Astral.App;

public partial class TargetSelectionWindow : Window
{
    private readonly TargetRegistry _registry;
    private readonly List<TargetCardState> _cards;
    private TargetCategory? _selectedCategory;

    public TargetSelectionWindow(
        TargetRegistry registry,
        TargetSelection currentSelection)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentNullException.ThrowIfNull(currentSelection);
        InitializeComponent();
        _cards = _registry.GetBuiltInTargets()
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
        RenderCategories();
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

    private void RenderCategories()
    {
        CategoryPanel.Children.Clear();
        AddCategoryButton("Popüler", null);
        AddCategoryButton("İletişim", TargetCategory.Communication);
        AddCategoryButton("Oyun", TargetCategory.Game);
        AddCategoryButton("Okuma/Yazı", TargetCategory.ReadingWriting);
        AddCategoryButton("Canlı/Sosyal", TargetCategory.LiveSocial);
        AddCategoryButton("Blog", TargetCategory.Blog);
        AddCategoryButton("Özel", TargetCategory.Custom);
    }

    private void AddCategoryButton(string label, TargetCategory? category)
    {
        var button = new WpfButton
        {
            Content = label,
            MinHeight = 30,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(12, 4, 12, 4),
            Tag = category
        };
        button.Click += (_, _) =>
        {
            _selectedCategory = category;
            RenderCards();
        };
        CategoryPanel.Children.Add(button);
    }

    private void RenderCards()
    {
        TargetCardsPanel.Children.Clear();
        var query = SearchBox.Text?.Trim();
        var filtered = _cards.Where(card =>
            (_selectedCategory is null || card.Target.Category == _selectedCategory)
            && (string.IsNullOrWhiteSpace(query)
                || card.Target.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)));

        foreach (var card in filtered)
        {
            var checkbox = new WpfCheckBox
            {
                IsChecked = card.IsSelected,
                Margin = new Thickness(0, 0, 12, 12),
                MinWidth = 184,
                MaxWidth = 224,
                MinHeight = 86,
                Padding = new Thickness(12),
                ToolTip = card.Target.Metadata.TryGetValue("note", out var note)
                    ? note
                    : card.Target.Label,
                Content = CreateCardContent(card.Target)
            };
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

    private static StackPanel CreateCardContent(TargetDefinition target)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = target.Label,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(new TextBlock
        {
            Text = target.ScopeLabel,
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 12,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(142, 200, 255))
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Hazır",
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(184, 199, 215))
        });
        return panel;
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
