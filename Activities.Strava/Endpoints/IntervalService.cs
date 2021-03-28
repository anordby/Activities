﻿using System.Linq;
using Activities.Strava.Endpoints.Models;

namespace Activities.Strava.Endpoints
{
    public static class IntervalService
    {
        // Update when logic is modified to trigger recalculation.
        private const string Version = "2021-03-28";

        public static bool TryTagIntervalLaps(this DetailedActivity activity)
        {
            if (activity._IntervalVersion == Version)
            {
                return false;
            }

            activity._IntervalVersion = Version;
                
            if (activity.Laps == null)
            {
                return true;
            }

            if (activity.Laps == null || activity.Laps.Count(lap => lap.Distance > 200 && lap.ElapsedTime > 60) < 6)
            {
                return true;
            }

            var laps = activity.Laps
                .Where(lap => lap.Distance > 200 && lap.ElapsedTime > 60)
                .ToList();
                
            var medianSpeed = laps
                .OrderBy(lap => lap.AverageSpeed)
                .Skip(laps.Count / 2)
                .First()
                .AverageSpeed;

            var speedDifference = 0.5;

            var intervalLaps = laps
                .Where(lap => lap.AverageSpeed >= medianSpeed - speedDifference)
                .ToList();
                
            var medianDistance = intervalLaps
                .OrderBy(lap => lap.Distance)
                .Skip(intervalLaps.Count / 2)
                .First()
                .Distance;
                
            intervalLaps = intervalLaps
                .Where(lap => lap.Distance >= medianDistance / 3 && lap.Distance <= medianDistance * 3)
                .ToList();

            foreach (var lap in intervalLaps)
            {
                lap.IsInterval = true;
            }

            return true;
        }
    }
}