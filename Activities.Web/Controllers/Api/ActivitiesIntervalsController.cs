﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Activities.Core.Extensions;
using Activities.Strava.Endpoints;
using Activities.Strava.Endpoints.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Activities.Web.Controllers.Api
{
    [ApiController]
    [Route("api/[controller]")]
    public class ActivitiesIntervalsController : ControllerBase
    {
        private readonly ActivitiesClient _activitiesClient;

        public ActivitiesIntervalsController(ActivitiesClient activitiesClient)
        {
            _activitiesClient = activitiesClient;
        }

        [HttpGet]
        public async Task<dynamic> Get(string type = "Run", string duration = "LastMonths", int year = 0, double? minPace = null, double? maxPace = null)
        {
            var stravaAthlete = await AuthenticationController.TryGetStravaAthlete(HttpContext);

            if (stravaAthlete == null)
            {
                return Unauthorized();
            }
            
            var activities = (await _activitiesClient.GetActivities(stravaAthlete.AccessToken, stravaAthlete.AthleteId)).AsEnumerable();

            if (type != null)
            {
                activities = activities.Where(activity => activity.Type == type);
            }

            if (duration == "LastMonths")
            {
                activities = activities.Where(activity => activity.StartDate >= DateTime.Today.GetStartOfWeek().AddDays(-7 * 20));
            }
            else if (duration == "LastYear")
            {
                activities = activities.Where(activity => activity.StartDate >= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 01).AddYears(-1));
            }
            else if (duration == "Year")
            {
                activities = activities.Where(activity => activity.StartDate.Year == year);
            }
            else
            {
                activities = activities.Where(activity => activity.StartDate.Year == DateTime.Today.Year);
            }

            var detailedActivities = await activities.ForEachAsync(4, activity => _activitiesClient.GetActivity(stravaAthlete.AccessToken, activity.Id));
            var intervalActivities = detailedActivities
                .Select(
                    activity => new
                    {
                        Activity = activity,
                        IntervalLaps = activity.Laps?
                            .Where(lap => IsIntervalWithinPace(lap, minPace, maxPace))
                            .ToList()
                    })
                .Where(activity => activity.IntervalLaps?.Any() == true)
                .ToList();

            var allLaps = intervalActivities.SelectMany(activity => activity.IntervalLaps).ToList();
            var maxDistance = allLaps.Any() ? allLaps.Max(lap => lap.Distance) : 0;
            var maxDuration = allLaps.Any() ? allLaps.Max(lap => lap.ElapsedTime) : 0;
            var maxSpeed = allLaps.Any() ? allLaps.Max(lap => lap.AverageSpeed) : 0;
            var maxHeartrate = allLaps.Any() ? allLaps.Max(lap => lap.AverageHeartrate) : 0;

            var intervals = intervalActivities
                .GroupBy(activity => GetGroupKey(activity.Activity, duration))
                .Select(month => new
                {
                    Date = month.Key,
                    Activities = month
                        .Select(activity =>
                        {
                            return new
                            {
                                activity.Activity.Id,
                                Date = activity.Activity.StartDate.ToString("ddd dd. MMM yyyy"),
                                activity.Activity.Name,
                                activity.Activity.Description,
                                Interval_AverageSpeed = activity.IntervalLaps.Average(lap => lap.AverageSpeed).ToMinPerKmString(),
                                Interval_AverageHeartrate = $"{activity.IntervalLaps.Average(lap => lap.AverageHeartrate):0} bpm",
                                Interval_Laps = GetLapsResult(activity.IntervalLaps, maxDistance, maxSpeed, maxHeartrate, maxDuration),
                                Laktat = GetLactate(activity.Activity)
                            };
                        })
                        .ToList()
                })
                .ToList();
            
            var measurements = intervalActivities
                .GroupBy(activity => GetGroupKey(activity.Activity, duration))
                .Select(month =>
                {
                    var measures = month
                        .Select(activity => GetLactate(activity.Activity))
                        .Where(activity => activity.Any())
                        .Select(activity => activity.Average())
                        .ToList();

                    var averageFileTime = (long)month.Average(activity => activity.Activity.StartDate.ToFileTime());
                    var averageDate = DateTime.FromFileTime(averageFileTime);
                    
                    return new
                    {
                        Date = averageDate.ToString("yyyy-MM-dd"),
                        Lactate = measures.Any() ? (double?)measures.Median() : null,
                        LactateMin = measures.Any() ? (double?)measures.Min() : null,
                        LactateMax = measures.Any() ? (double?)measures.Max() : null
                    };
                })
                .Where(item => item.Lactate.HasValue)
                .ToList();
            
            var allMeasurements = intervalActivities
                .Select(activity => new
                {
                    Date = activity.Activity.StartDate,
                    Lactate = GetLactate(activity.Activity)
                })
                .SelectMany(activity => activity.Lactate.Select(lactate => new
                {
                    Date = activity.Date,
                    Lactate = lactate
                }))
                .ToList();
            
            var distances = detailedActivities
                .GroupBy(activity => GetGroupKey(activity, duration))
                .Select(month =>
                {
                    var intervalDistance = Math.Round(month.Where(activity => activity.Laps != null).Sum(activity => activity.Laps.Where(lap => IsIntervalWithinPace(lap, minPace, maxPace)).Sum(lap => lap.Distance)) / 1000, 2);
                    
                    return new
                    {
                        Date = month.Key,
                        NonIntervalDistance = Math.Round((month.Sum(activity => activity.Distance) / 1000) - intervalDistance, 2),
                        IntervalDistance = intervalDistance
                    };
                })
                .ToList();
            
            var paces = intervalActivities
                .GroupBy(activity => GetGroupKey(activity.Activity, duration))
                .Select(month =>
                {
                    var averagePace = month.Average(activity => activity.Activity.Laps.Where(lap => IsIntervalWithinPace(lap, minPace, maxPace)).Average(lap => lap.AverageSpeed));
                    
                    return new
                    {
                        Date = month.Key,
                        IntervalPace = averagePace,
                        Label = $"{month.Key} - {averagePace.ToMinPerKmString()} ({month.Count()} activities)"
                    };
                })
                .ToList();

            return new
            {
                Intervals = intervals,
                Measurements = measurements,
                AllMeasurements = allMeasurements,
                Distances = distances,
                Paces = paces
            };
        }

        private string GetGroupKey(DetailedActivity activity, string duration)
        {
            if (duration == "LastMonths")
            {
                var startOfWeek = activity.StartDate.GetStartOfWeek();
                var endOfWeek = startOfWeek.AddDays(6);

                if (startOfWeek.Month == endOfWeek.Month)
                {
                    return $"{startOfWeek:dd.} - {startOfWeek.AddDays(6):dd. MMM yyyy}";
                }
                
                return $"{startOfWeek:dd. MMM} - {startOfWeek.AddDays(6):dd. MMM yyyy}";
            }
            else if (duration == "LastYear" || duration == "Year")
            {
                return activity.StartDate.ToString("MMM yyyy");
            }

            return "";
        }

        private List<LapResult> GetLapsResult(List<Lap> laps, double? maxDistance = null, double? maxSpeed = null, double? maxHeartrate = null, double? maxDuration = null)
        {
            if (laps == null)
            {
                return new List<LapResult>();
            }

            var maxLapDistance = maxDistance ?? laps.Max(lap => lap.Distance);
            var maxLapDuration = maxDuration ?? laps.Max(lap => lap.ElapsedTime);
            var maxLapSpeed = maxSpeed ?? laps.Max(lap => lap.AverageSpeed);
            var maxLapHeartrate = maxHeartrate ?? laps.Max(lap => lap.AverageHeartrate);
            
            return laps
                .Select(lap => new LapResult(lap, maxLapDistance, maxLapSpeed, maxLapHeartrate, maxLapDuration))
                .ToList();
        }

        private bool IsIntervalWithinPace(Lap lap, double? minPace, double? maxPace)
        {
            return lap.IsInterval && (minPace == null || lap.AverageSpeed >= minPace.Value.ToMetersPerSecond()) && (maxPace == null || lap.AverageSpeed <= maxPace.Value.ToMetersPerSecond());
        }

        private IReadOnlyList<double> GetLactate(DetailedActivity activity)
        {
            var result = new List<double>();

            if (activity.Lactate.HasValue)
            {
                result.Add(activity.Lactate.Value);
            }

            if (activity.Laps == null)
            {
                return result;
            }

            foreach (var lap in activity.Laps)
            {
                if (lap.Lactate.HasValue)
                {
                    result.Add(lap.Lactate.Value);
                }
            }

            return result;
        }
    }

    public class LapResult
    {
        public LapResult(Lap lap, double maxDistance, double maxSpeed, double maxHeartrate, double maxDuration)
        {
            Id = lap.Id;
            Distance = lap.Distance.ToKmString();
            AverageSpeed = lap.AverageSpeed.ToMinPerKmString();
            Heartrate = $"{lap.AverageHeartrate:0} bpm";
            Duration = TimeSpan.FromSeconds(lap.ElapsedTime).ToString(@"mm\:ss");
            Lactate = lap.Lactate?.ToString("0.0");

            DistanceFactor = 1.0 / Math.Round(maxDistance / 1000, 1) * Math.Round(lap.Distance / 1000, 1);
            AverageSpeedFactor = 1.0 / maxSpeed * lap.AverageSpeed;
            HeartrateFactor = Math.Max(1.0 / (maxHeartrate - 100) * (lap.AverageHeartrate - 100), 0.0);
            DurationFactor = 1.0 / maxDuration * lap.ElapsedTime;
        }

        public long Id { get; init; }
        public string Distance { get; init; }
        public string AverageSpeed { get; init; }
        public string Heartrate { get; init; }
        public string Duration { get; init; }
        public string Lactate { get; init; }
        
        public double DistanceFactor { get; init; }
        public double AverageSpeedFactor { get; init; }
        public double HeartrateFactor { get; init; }
        public double DurationFactor { get; init; }
    }
}
