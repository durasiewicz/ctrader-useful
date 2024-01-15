using System;
using System.Collections.Concurrent;
using System.Linq;
using cAlgo.API;

namespace cAlgo
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class Base : Indicator
    {
        private readonly string _upperZoneLinePrefix = "up-zone-line-";
        private readonly string _lowerZoneLinePrefix = "low-zone-line-";
        private readonly Color _darkColor = Color.Black;
        private readonly Color _lightColor = Color.White;

        private Color IndicatorColor
        {
            get { return Chart.ColorSettings.BackgroundColor == Color.Black ? _lightColor : _darkColor; }
        }

        private static readonly ConcurrentDictionary<string, DateTime> s_firstVisibleBarDateTime = new ConcurrentDictionary<string, DateTime>();
        private static readonly TimeFrame[] s_switchableTimeFrames = new TimeFrame[] 
        {
            TimeFrame.Minute,
            TimeFrame.Minute2,
            TimeFrame.Minute3,
            TimeFrame.Minute4,
            TimeFrame.Minute5,
            TimeFrame.Minute6,
            TimeFrame.Minute7,
            TimeFrame.Minute8,
            TimeFrame.Minute9,
            TimeFrame.Minute10,
            TimeFrame.Minute15,
            TimeFrame.Minute20,
            TimeFrame.Minute30,
            TimeFrame.Minute45,
            TimeFrame.Hour,
            TimeFrame.Hour2,
            TimeFrame.Hour3,
            TimeFrame.Hour4,
            TimeFrame.Hour6,
            TimeFrame.Hour8,
            TimeFrame.Hour12,
            TimeFrame.Daily,
            TimeFrame.Day2,
            TimeFrame.Day3,
            TimeFrame.Weekly,
            TimeFrame.Monthly
        };

        protected override void Initialize()
        {
            if (!SetScroll()) return;
            InitBaseHandlers();
            InitCustomGui();
        }

        private void InitBaseHandlers()
        {
            Chart.ObjectsAdded += Chart_ObjectsAdded;
            Chart.ObjectsRemoved += Chart_ObjectsRemoved;
            Chart.ObjectsUpdated += Chart_ObjectsUpdated;

            foreach (var obj in Chart.Objects.ToList())
            {
                if (obj is ChartRectangle rect)
                    AddOrUpdateZoneLines(rect);
            }
        }

        private void InitCustomGui()
        {
            var scrollButton = new Button 
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Text = ">>"
            };

            scrollButton.Click += a => Chart.ScrollXTo(Bars.Count);

            var intervalButton = new Button 
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Text = Chart.TimeFrame.ShortName.ToUpper(),
                FontSize = 24
            };

            Chart.AddHotkey(() => SetTimeFrame(1), "w");
            Chart.AddHotkey(() => SetTimeFrame(-1), "s");
            Chart.AddHotkey(() => Chart.ScrollXTo(Bars.Count), "d");
            Chart.AddControl(scrollButton);
            Chart.AddControl(intervalButton);
        }

        private void SetTimeFrame(int shift)
        {
            var currentTimeFrameIndex = -1;

            for (int index = 0; index < s_switchableTimeFrames.Length; index++)
            {
                if (s_switchableTimeFrames[index] == Chart.TimeFrame)
                    currentTimeFrameIndex = index;
            }

            currentTimeFrameIndex += shift;

            if (currentTimeFrameIndex < 0)
                currentTimeFrameIndex = 0;
            else if (currentTimeFrameIndex > s_switchableTimeFrames.Length - 1)
                currentTimeFrameIndex = s_switchableTimeFrames.Length - 1;

            s_firstVisibleBarDateTime.AddOrUpdate(Chart.SymbolName, 
                Bars[Chart.FirstVisibleBarIndex].OpenTime, 
                (k, v) => Bars[Chart.FirstVisibleBarIndex].OpenTime);
        
            Chart.TryChangeTimeFrame(s_switchableTimeFrames[currentTimeFrameIndex]);
        }

        private bool SetScroll()
        {
            var result = true;
        
            if (s_firstVisibleBarDateTime.TryGetValue(Chart.SymbolName, out var firstVisibleBarDateTime))
            {
                var currentFirstBar = Bars.First();

                if (currentFirstBar.OpenTime > firstVisibleBarDateTime)
                {
                    Bars.LoadMoreHistory();
                    result = false;
                }
                else
                {
                    Chart.ScrollXTo(firstVisibleBarDateTime);
                    s_firstVisibleBarDateTime.TryRemove(Chart.SymbolName, out var _);
                }
            }
            
            return result;
        }

        private void Chart_ObjectsUpdated(ChartObjectsUpdatedEventArgs args)
        {
            foreach (var obj in args.ChartObjects)
            {
                if (obj is ChartRectangle rect)
                    AddOrUpdateZoneLines(rect);
            }
        }
        
        private void Chart_ObjectsRemoved(ChartObjectsRemovedEventArgs args)
        {
            foreach (var obj in args.ChartObjects)
            {
                if (obj is ChartRectangle rect)
                {
                    Chart.RemoveObject(_upperZoneLinePrefix + rect.Name);
                    Chart.RemoveObject(_lowerZoneLinePrefix + rect.Name);
                }
            }
        }

        private static readonly double[] s_visibleFibLevels = new double[] { 0, 50, 100 };

        private void Chart_ObjectsAdded(ChartObjectsAddedEventArgs args)
        {
            foreach (var obj in args.ChartObjects)
            {
                if (obj is ChartRectangle rect)
                    AddOrUpdateZoneLines(rect);

                if (obj is ChartFibonacciRetracement fib)
                {
                    fib.Color = IndicatorColor;
                    fib.DisplayPrices = false;
                    fib.Thickness = 3;
                    
                    foreach(var level in fib.FibonacciLevels)
                    {
                        if (s_visibleFibLevels.Contains(level.PercentLevel))
                            level.IsVisible = true;
                        else 
                            level.IsVisible = false;
                    }
                }
            }
        }

        private void AddOrUpdateZoneLines(ChartRectangle rect)
        {
            DateTime startTime;
            DateTime endTime;

            if (rect.Time1 < rect.Time2)
            {
                startTime = rect.Time1;
                endTime = rect.Time2;
            }
            else
            {
                startTime = rect.Time2;
                endTime = rect.Time1;
            }

            var upperZoneLine = Chart.DrawTrendLine(_upperZoneLinePrefix + rect.Name, startTime, rect.Y1, endTime, rect.Y1, IndicatorColor, 5);
            var lowerZoneLint = Chart.DrawTrendLine(_lowerZoneLinePrefix + rect.Name, startTime, rect.Y2, endTime, rect.Y2, IndicatorColor, 5);

            upperZoneLine.ExtendToInfinity = lowerZoneLint.ExtendToInfinity = true;
            rect.IsFilled = true;
        }

        public override void Calculate(int index)
        {
            var currentBar = Bars[index];
            
            if (currentBar.High - currentBar.Low <= 0 || Math.Abs(currentBar.Open - currentBar.Close) / Math.Abs(currentBar.High - currentBar.Low) >= 0.5)
            {
                return;
            }
            
            Chart.SetBarColor(index, IndicatorColor);
        }
    }
}