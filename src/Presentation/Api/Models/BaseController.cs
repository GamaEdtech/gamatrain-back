namespace GamaEdtech.Presentation.Api.Models
{
    using GamaEdtech.Presentation.Api.Filters;

    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [ApiResultFilter]
    [Route("/v{version:apiVersion}/[controller]")]
    public class BaseController : ControllerBase
    {
        public bool? UserIsAuthenticated => HttpContext?.User?.Identity?.IsAuthenticated;
    }
}
