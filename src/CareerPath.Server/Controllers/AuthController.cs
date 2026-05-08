using CareerPath.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CareerPath.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _userManager;

    public AuthController(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthStatusDto>> Login([FromBody] LoginRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid request." });

        // PasswordSignInAsync wants the user name; we look up the user by email and
        // pass user.UserName. (When we seeded the user we set UserName = Email so
        // they'll match, but this is the robust way to support either.)
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user == null) return Unauthorized(new { error = "Invalid email or password." });

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName!,
            req.Password,
            isPersistent: req.RememberMe,
            lockoutOnFailure: false);

        if (!result.Succeeded) return Unauthorized(new { error = "Invalid email or password." });

        return Ok(new AuthStatusDto
        {
            IsAuthenticated = true,
            Email = user.Email ?? ""
        });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [AllowAnonymous]
    public ActionResult<AuthStatusDto> Me()
    {
        var isAuth = User.Identity?.IsAuthenticated ?? false;
        return Ok(new AuthStatusDto
        {
            IsAuthenticated = isAuth,
            Email = isAuth ? (User.Identity!.Name ?? "") : ""
        });
    }
}
