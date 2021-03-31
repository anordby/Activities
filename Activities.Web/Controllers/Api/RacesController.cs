using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Activities.Core.Extensions;
using Activities.Strava.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Activities.Web.Controllers.Api
{
    [ApiController]
    [Route("api/[controller]")]
    public class RacesController : ControllerBase
    {
        private readonly ActivitiesClient _activitiesClient;

        public RacesController(ActivitiesClient activitiesClient)
        {
            _activitiesClient = activitiesClient;
        }

        [HttpGet]
        public async Task<dynamic> Get()
        {
            var stravaAthlete = await AuthenticationController.TryGetStravaAthlete(HttpContext);

            if (stravaAthlete == null)
            {
                return Unauthorized();
            }
            
            var races = await _activitiesClient.GetActivities(stravaAthlete.AccessToken, stravaAthlete.AthleteId);

            return races
                .Select(

                    activity => new
                    {
                        activity.Id,
                        activity.Name,
                        activity.WorkoutType,
                        MovingTime = activity.MovingTime.ToTimeStringSeconds(),
                        StartDate = activity.StartDate.ToString("dd.MM.yyyy"),
                        Distance = activity.Distance.ToKmString(),
                        AverageSpeed = activity.AverageSpeed.ToMinPerKmString()
                    })
                .Where(x => x.WorkoutType == 1)
                .ToList();
        }
    }
}