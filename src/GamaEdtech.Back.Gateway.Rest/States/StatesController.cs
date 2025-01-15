using Dapper;
using GamaEdtech.Back.DataSource.Utils;
using GamaEdtech.Back.Domain.Base;
using GamaEdtech.Back.Domain.Cities;
using GamaEdtech.Back.Domain.Countries;
using GamaEdtech.Back.Domain.States;
using GamaEdtech.Back.Gateway.Rest.Common;
using GamaEdtech.Back.Gateway.Rest.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace GamaEdtech.Back.Gateway.Rest.States;

[Route("api/[controller]")]
[ApiController]
public class StatesController : ControllerBase
{
	private readonly ConnectionString _connectionString;
	private readonly GamaEdtechDbContext _dbContext;
	private readonly ICountryRepository _countryRepository;
	private readonly IStateRepository _stateRepository;
	private readonly ICityRepository _cityRepository;

	public StatesController(
		ConnectionString connectionString,
		GamaEdtechDbContext dbContext,
		ICountryRepository countryRepository,
		IStateRepository stateRepository,
		ICityRepository cityRepository)
	{
		_connectionString = connectionString;
		_dbContext = dbContext;
		_countryRepository = countryRepository;
		_stateRepository = stateRepository;
		_cityRepository = cityRepository;
	}

	///<summary>
	/// Find States
	///</summary>
	/// 
	///<remarks>
	/// Sample request:
	/// 
	///     GET /States
	///     
	///		Query params:
	///		{
	///			"page": int - nullablr,
	///			"pageSize": int - nullablr, 
	///			"countryId": int - nullable
	///			"sortBy": "Name" or "Code" - Default("Name"),
	///			"order": "ASC" or "DESC" - Default("ASC"),
	///		}
	///</remarks>
	///
	///<response code="200">Returns list of states 
	///						(returns empty list if no state is found based on search queries)
	///</response>
	///<response code="500">Server error</response>
	[HttpGet]
	[PaginationTransformer]
	[SortingTransformer(DefaultSortKey = "Name", ValidSortKeys = "Name,Code")]
	public async Task<IActionResult> List(
		[FromQuery] FilterStatesDto filtering,
		[FromQuery] SortingDto sorting,
		[FromQuery] PaginationDto pagination)
	{
		var query = @"
            SELECT [Id], [Name], [Code], [CountryId]
            FROM [dbo].[State] "
			+ (filtering.CountryId.HasValue ?  @"WHERE [CountryId] = @CountryId " : " ") +
            "ORDER BY [" + sorting.SortBy + "] " + sorting.Order + @"
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

		using (var connection = new SqlConnection(_connectionString.Value))
		{
			var contries = await connection.QueryAsync<StateInListDto>(
				query,
				new
				{
					CountryId = filtering.CountryId,
					Offset = (pagination.Page - 1) * pagination.PageSize,
					PageSize = pagination.PageSize,
				});

			return Ok(Envelope.Ok(contries));
		}
	}

	///<summary>
	/// Add state to country
	///</summary>
	/// 
	///<remarks>
	/// Sample request:
	///
	///     POST /States
	///     
	///     Request body:
	///     {
	///		  "name": Required - MaxLenght(50),
	///		  "code": Required - ISO Alpha-2/Alpha-3,
	///		  "countryId": Required - int
	///		}
	///</remarks>
	///
	///<response code="201"></response>
	///<response code="400"></response>
	///<response code="404"></response>
	///<response code="500">Server error</response>
	[HttpPost]
	public async Task<IActionResult> AddStateToCountry([FromBody] AddStateDto dto)
	{
		var country = await _countryRepository.GetBy(new Id(dto.CountryId));

		if (country is null)
			return NotFound();

		if (await _stateRepository.ContainsStateWithNameInCountry(dto.Name, country.Id))
			return BadRequest(Envelope.Error("name is duplicate"));

		if (await _stateRepository.ContainsStateWithCodeInCountry(dto.Code, country.Id))
			return BadRequest(Envelope.Error("code is duplicate"));

		var state = new State(dto.Name, dto.Code, country.Id);

		await _stateRepository.Add(state);
		await _dbContext.SaveChangesAsync();

		return Created();
	}

	///<summary>
	/// Edit state info (name and code)
	///</summary>
	/// 
	///<remarks>
	/// Sample request:
	///
	///     PUT /States/{id:int}/EditInfo
	///     
	///     Request body:
	///     {
	///		  "name": Required - MaxLenght(50),
	///		  "code": Required - ISO Alpha-2/Alpha-3
	///		}
	///</remarks>
	///
	///<response code="204"></response>
	///<response code="400"></response>
	///<response code="404"></response>
	///<response code="500">Server error</response>
	[HttpPut("{id:int}")]
	public async Task<IActionResult> EditInfo(
		[FromRoute] int id, [FromBody] EditStateInfoDto dto)
	{
		var state = await _stateRepository.GetBy(new Id(id));

		if (state is null)
			return NotFound();

		if (dto.Name != state.Name && await _stateRepository.ContainsStateWithNameInCountry(dto.Name, state.CountryId))
			return BadRequest(Envelope.Error("name is duplicate"));

		if (dto.Code != state.Code && await _stateRepository.ContainsStateWithCodeInCountry(dto.Code, state.CountryId))
			return BadRequest(Envelope.Error("code is duplicate"));

		state.EditInfo(dto.Name, dto.Code);

		await _dbContext.SaveChangesAsync();

		return NoContent();
	}

	///<summary>
	/// Move state to another country
	///</summary>
	/// 
	///<remarks>
	/// Sample request:
	///
	///     PUT /States/{id:int}/ChangeCountry
	///     
	///     Request body:
	///     {
	///		  "countryId": Required - int
	///		}
	///</remarks>
	///
	///<response code="204"></response>
	///<response code="400"></response>
	///<response code="404"></response>
	///<response code="500">Server error</response>
	[HttpPut("{id:int}/ChangeCountry")]
	public async Task<IActionResult> MoveStateToAnotherCountry(
		[FromRoute] int id, [FromBody] MoveStateToAnotherDto dto)
	{
		var state = await _stateRepository.GetBy(new Id(id));

		if (state is null)
			return NotFound();

		var country = await _countryRepository.GetBy(new Id(dto.CountryId));

		if (country is null)
			return NotFound();

		if (await _stateRepository.ContainsStateWithNameInCountry(state.Name, country.Id))
			return BadRequest(Envelope.Error("State's name is duplicate in target country"));

		if (await _stateRepository.ContainsStateWithCodeInCountry(state.Code, country.Id))
			return BadRequest(Envelope.Error("State's code is duplicate in target country"));

		state.MoveTo(country);

		await _dbContext.SaveChangesAsync();

		return NoContent();
	}

	///<summary>
	/// Remove state
	///</summary>
	/// 
	///<remarks>
	/// Sample request:
	///
	///     DELETE /States/{id:int}
	///</remarks>
	///
	///<response code="204"></response>
	///<response code="400"></response>
	///<response code="404"></response>
	///<response code="500">Server error</response>
	[HttpDelete("{id:int}")]
	public async Task<IActionResult> Remove([FromRoute] int id)
	{
		var state = await _stateRepository.GetBy(new Id(id));

		if (state is null)
			return NotFound();

		if (await _cityRepository.ContainsCityInStateWith(state.Id))
			return BadRequest(Envelope.Error("State has related cities"));

		await _stateRepository.Remove(state);
		await _dbContext.SaveChangesAsync();

		return NoContent();
	}
}
