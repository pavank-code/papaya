using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FlowVault.UI.Services;
using FlowVault.Shared.Models;

namespace FlowVault.UI.Controls;

/// <summary>
/// Tile displaying calendar/schedule view
/// </summary>
public class CalendarTile : TileBase
{
    private readonly BackendClient _backend;
    private CalendarView? _calendar;
    private ListView? _eventsList;

    public CalendarTile(BackendClient backend)
    {
        _backend = backend;
        
        Content = CreateTileContainer(CreateContent());
        _ = LoadDataAsync();
    }

    protected override string GetTileTitle() => "Calendar";

    private FrameworkElement CreateContent()
    {
        var panel = new StackPanel { Spacing = 8 };

        _calendar = new CalendarView
        {
            SelectionMode = CalendarViewSelectionMode.Single,
            MaxHeight = 250
        };
        _calendar.SelectedDatesChanged += Calendar_SelectedDatesChanged;
        panel.Children.Add(_calendar);

        var eventsHeader = new TextBlock
        {
            Text = "Today's Events",
            Style = (Style)Application.Current.Resources["TileSubtitleStyle"],
            Margin = new Thickness(0, 8, 0, 4)
        };
        panel.Children.Add(eventsHeader);

        _eventsList = new ListView
        {
            MaxHeight = 150
        };
        panel.Children.Add(_eventsList);

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private async void Calendar_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (args.AddedDates.Count > 0)
        {
            var selectedDate = args.AddedDates[0].Date;
            await LoadEventsForDateAsync(selectedDate);
        }
    }

    private async Task LoadDataAsync()
    {
        await LoadEventsForDateAsync(DateTime.Today);
    }

    private async Task LoadEventsForDateAsync(DateTime date)
    {
        try
        {
            var events = await _backend.GetCalendarEventsAsync(date.Date, date.Date.AddDays(1));
            if (events != null && _eventsList != null)
            {
                var items = events.Select(e => new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = e.Title,
                            Style = (Style)Application.Current.Resources["TileBodyStyle"]
                        },
                        new TextBlock
                        {
                            Text = $"{e.StartTime:HH:mm} - {e.EndTime:HH:mm}",
                            Style = (Style)Application.Current.Resources["TileSubtitleStyle"]
                        }
                    }
                }).ToList();

                _eventsList.ItemsSource = items;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load events: {ex.Message}");
        }
    }
}
