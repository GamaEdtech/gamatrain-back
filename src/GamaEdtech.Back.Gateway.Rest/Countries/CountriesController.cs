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

namespace GamaEdtech.Back.Gateway.Rest.Countries;

[Route("api/[controller]")]
[ApiController]
public class CountriesController : ControllerBase
{
	private readonly ConnectionString _connectionString;
	private readonly GamaEdtechDbContext _dbCotext;
	private readonly ICountryRepository _countryRepository;
	private readonly IStateRepository _stateRepository;
	private readonly ICityRepository _cityRepository;

	public CountriesController(
		ConnectionString connectionString,
		GamaEdtechDbContext dbCotext, 
		ICountryRepository countryRepository,
		IStateRepository stateRepository,
		ICityRepository cityRepository)
	{
		_connectionString = connectionString;
		_dbCotext = dbCotext;
		_countryRepository = countryRepository;
		_stateRepository = stateRepository;
		_cityRepository = cityRepository;
	}

	///<summary>
	/// List Countries (sort and paginate them)
	///</summary>
	/// 
	///<remarks>
	/// Sample request:
	/// 
	///     GET /Countries
	///     
	///		Query params:
	///		{
	///			"page": int - nullable,
	///			"pageSize": int - nullable, 
	///			"sortBy": "Name" or "Code" - Default("Name"),
	///			"order": "ASC" or "DESC" - Default("ASC"),
	///		}
	///</remarks>
	///
	///<response code="200">Returns list of countries 
	///						(returns empty list if no country is found based on search queries)
	///</response>
	///<response code="500">Server error</response>
	[HttpGet]
	[PaginationTransformer]
	[SortingTransformer(DefaultSortKey = "Name", ValidSortKeys = "Name,Code")]
	public async Task<IActionResult> List(
		[FromQuery] PaginationDto pagination,
		[FromQuery] SortingDto sorting)
	{
		var query = @"
            SELECT [Id], [Name], [Code]
            FROM [dbo].[Country]" +
            "ORDER BY [" + sorting.SortBy + "] " + sorting.Order + @"
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

		using (var connection = new SqlConnection(_connectionString.Value))
		{
			var countries = await connection.QueryAsync<ContryInListDto>(
				query,
				new 
				{
					Offset = (pagination.Page - 1) * pagination.PageSize, 
					PageSize = pagination.PageSize,
				});

			return Ok(Envelope.Ok(countries));
		}
	}

	///<summary>
	/// Add Country
	///</summary>
	/// 
	///<remarks>
	/// Sample request:
	///
	///     POST /Countries
	///     
	///     Request body:
	///     {
	///		  "name": Required - MaxLenght(50),
	///		  "code": Required - ISO Alpha-2/Alpha-3
	///		}
	///</remarks>
	///
	///<response code="201"></response>
	///<response code="400"></response>
	///<response code="500">Server error</response>
	[HttpPost]
	public async Task<IActionResult> Add([FromBody] AddCountryDto dto)
	{ 
		if (await _countryRepository.ContainsCountrywithName(dto.Name))
			return BadRequest(Envelope.Error("name is duplicate"));

		if (await _countryRepository.ContainsCountrywithCode(dto.Code))
			return BadRequest(Envelope.Error("code is duplicate"));

		var country = new Country(dto.Name, dto.Code);

		await _countryRepository.Add(country);
		await _dbCotext.SaveChangesAsync();

		return Created();
	}

	///<summary>
	/// Edit Country info
	///</summary>
	/// 
	///<remarks>
	/// Sample request:
	///
	///     PATCH /Countries/{id:int}
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
	[HttpPut("{id:guid}")]
	public async Task<IActionResult> EditInfo(
		[FromRoute] int id, [FromBody] EditCountryInfoDto dto)
	{
		var country = await _countryRepository.GetBy(new Id(id));

		if (country is null)
			return NotFound();

		if (dto.Name != country.Name && await _countryRepository.ContainsCountrywithName(dto.Name))
			return BadRequest(Envelope.Error("name is duplicate"));

		if (dto.Code != country.Code && await _countryRepository.ContainsCountrywithCode(dto.Code))
			return BadRequest(Envelope.Error("code is duplicate"));

		country.EditInfo(dto.Name, dto.Code);

		await _dbCotext.SaveChangesAsync();

		return NoContent();
	}

	///<summary>
	/// Remove Country
	///</summary>
	/// 
	///<remarks>
	/// Sample request:
	///
	///     DELETE /Countries/{id:int}
	///</remarks>
	///
	///<response code="204"></response>
	///<response code="400"></response>
	///<response code="404"></response>
	///<response code="500">Server error</response>
	[HttpDelete("{id:int}")]
	public async Task<IActionResult> Remove([FromRoute] int id)
	{
		var country = await _countryRepository.GetBy(new Id(id));

		if (country is null)
			return NotFound();

		if (await _stateRepository.ContainsStateInCountryWith(country.Id))
			return BadRequest(Envelope.Error("Country has related states"));

		if (await _cityRepository.ContainsCityInCountryWith(country.Id))
			return BadRequest(Envelope.Error("Country has related cities"));

		await _countryRepository.Remove(country);
		await _dbCotext.SaveChangesAsync();

		return NoContent();
	}
}


