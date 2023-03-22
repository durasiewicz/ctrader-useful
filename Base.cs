using System;
using System.Collections.Concurrent;
using System.Linq;
using cAlgo.API;

namespace cAlgo
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class Base : Indicator
    {
        /// <summary>
        /// Upper zone line name prefix.
        /// </summary>
        private readonly string _upperZoneLinePrefix = "up-zone-line-";
        
        /// <summary>
        /// Lower zone line name prefix.
        /// </summary>
        private readonly string _lowerZoneLinePrefix = "low-zone-line-";
        
        /// <summary>
        /// Dark color for zone lines.
        /// </summary>
        private readonly Color _darkColor = Color.Black;
        
        /// <summary>
        /// Light color for zone lines.
        /// </summary>
        private readonly Color _lightColor = Color.White;

        /// <summary>
        /// Predefined colors for both light and dark chart themes.
        /// </summary>
        private Color IndicatorColor
        {
            get { return Chart.ColorSettings.BackgroundColor == Color.Black ? _lightColor : _darkColor; }
        }

        /// <summary>
        /// Stores last scroll position. It's need to be static, beacuse cTrader creates new indicator instance on timeframe change.
        /// </summary>
        private static readonly ConcurrentDictionary<string, DateTime> s_firstVisibleBarDateTime = new ConcurrentDictionary<string, DateTime>();
        
        /// <summary>
        /// Predefined timeframes.
        /// </summary>
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

        /// <summary>
        /// Base events and existing objects initialization.
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Creates button for chart scrolling to beginning and some useful shortcuts for timeframe changing.
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Changes timeframe based on predefined array.
        /// </summary>
        /// <param name="shift">Predefined timeframe slots shift. Value > 0 changes to bigger timeframe; Value < 0 changes to lower timeframe</param>
        /// <returns></returns>
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

        /// <summary>
        /// Restores scroll position after timeframe change.
        /// </summary>
        /// <returns>True when scroll is on the original position</returns>
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

        /// <summary>
        /// Updates zone lines associated to selected rectangle.
        /// </summary>
        /// <param name="args">Updated objects event args</param>
        /// <returns></returns>
        private void Chart_ObjectsUpdated(ChartObjectsUpdatedEventArgs args)
        {
            foreach (var obj in args.ChartObjects)
            {
                if (obj is ChartRectangle rect)
                    AddOrUpdateZoneLines(rect);
            }
        }
        
        /// <summary>
        /// Deletes zone lines associated to selected rectangle.
        /// </summary>
        /// <param name="args">Removed objects event args</param>
        /// <returns></returns>
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

        /// <summary>
        /// Visible fibonacci retracement levels.
        /// </summary>
        private static readonly double[] s_visibleFibLevels = new double[] { 0, 50, 100 };

        /// <summary>
        /// Configures created objects.
        /// 
        /// 1. For rectangle creates zone lines (for Supply and Demand technical analysis),
        /// 2. For fibonacci retracment changes default parameters to prefered by me (cTrader can't save this on its own (: ).
        /// </summary>
        /// <param name="args">Created charts objects event</param>
        /// <returns></returns>
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

        /// <summary>
        /// Creates or updates zone lines for rectangle.
        /// </summary>
        /// <param name="rect">Rectangle object</param>
        /// <returns></returns>
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

        /// <summary>
        /// Nothing interesting. It's only required by contract.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public override void Calculate(int index)
        {
            // empty
        }
    }
}
