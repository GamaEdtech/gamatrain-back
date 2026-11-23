namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.School;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// Who said they are what at which school (a school page's "I'm a teacher / student / parent..."): the memberships and how
    /// many each role has. Admins only and not delegable: it shows members' names and emails. Read-only: a membership is the
    /// user's own claim. See docs/business/schools-directory.md, "School members".
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/school-members")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class SchoolMembersController(Lazy<ILogger<SchoolMembersController>> logger, Lazy<ISchoolMemberService> schoolMemberService)
        : ApiControllerBase<SchoolMembersController>(logger)
    {
        /// <summary>Memberships, newest first: <c>role</c> (Teacher, Staff, Student, Parent, Alumni, or its number), <c>schoolId</c>,
        /// <c>userId</c>, <c>text</c> (the member's name, user name or email, or the school's name), <c>page</c>, <c>pageSize</c>
        /// (max 100). Also the count per role, and the distinct users and schools, of all matching ones.</summary>
        [HttpGet(""), Produces<ApiResponse<SchoolMemberAdminListDto>>()]
        public async Task<IActionResult<SchoolMemberAdminListDto>> GetMemberships([FromQuery] string? role, [FromQuery] long? schoolId,
            [FromQuery] long? userId, [FromQuery] string? text, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                SchoolMemberRole? memberRole = null;
                if (!string.IsNullOrWhiteSpace(role))
                {
                    memberRole = role.TryGetFromNameOrValue<SchoolMemberRole, byte>(out var parsed) ? parsed : null;
                    if (memberRole is null)
                    {
                        return Ok<SchoolMemberAdminListDto>(new(new Error { Message = "Unknown role.", Reference = "role" }));
                    }
                }

                var result = await schoolMemberService.Value.GetMembershipsAsync(new()
                {
                    Role = memberRole,
                    SchoolId = schoolId,
                    UserId = userId,
                    Text = text,
                    Page = page,
                    PageSize = pageSize,
                });
                return Ok<SchoolMemberAdminListDto>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<SchoolMemberAdminListDto>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
