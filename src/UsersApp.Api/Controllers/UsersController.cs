using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UsersApp.Data;

namespace UsersApp.Api.Controllers;

public record UserDto(int Id, string Name, string Email);

public class UserRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(200)]
    public string Email { get; set; } = string.Empty;
}

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db) => _db = db;

    // GET /api/users: список користувачів
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetAll()
    {
        var users = await _db.Users
            .OrderBy(u => u.Id)
            .Select(u => new UserDto(u.Id, u.Name, u.Email))
            .ToListAsync();
        return Ok(users);
    }

    // POST /api/users: додати користувача
    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] UserRequest request)
    {
        var user = new UserEntity { Name = request.Name.Trim(), Email = request.Email.Trim() };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return Created($"/api/users/{user.Id}", new UserDto(user.Id, user.Name, user.Email));
    }

    // PUT /api/users/{id}: редагувати користувача
    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, [FromBody] UserRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();

        user.Name = request.Name.Trim();
        user.Email = request.Email.Trim();
        await _db.SaveChangesAsync();
        return Ok(new UserDto(user.Id, user.Name, user.Email));
    }

    // DELETE /api/users/{id}: видалити користувача
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
