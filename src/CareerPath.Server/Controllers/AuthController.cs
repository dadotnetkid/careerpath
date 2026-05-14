using System.Security.Claims;
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

        return Ok(await BuildStatusAsync(user));
    }

    /// <summary>
    /// Self-service registration for CITE students. Always creates a user in the
    /// "Student" role — there is no public path to the "Admin" role (Admin is
    /// seeded once from appsettings and managed out-of-band).
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthStatusDto>> Register([FromBody] RegisterRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { error = "Email and password (min 8 chars) are required." });

        var existing = await _userManager.FindByEmailAsync(req.Email);
        if (existing != null)
            return Conflict(new { error = "An account with that email already exists." });

        var user = new IdentityUser
        {
            UserName = req.Email,
            Email = req.Email,
            EmailConfirmed = true
        };
        var create = await _userManager.CreateAsync(user, req.Password);
        if (!create.Succeeded)
            return BadRequest(new { error = string.Join(" ", create.Errors.Select(e => e.Description)) });

        await _userManager.AddToRoleAsync(user, AuthRoles.Student);

        // Stash the display name as a claim so we don't need an ApplicationUser subclass
        // and corresponding schema change.
        if (!string.IsNullOrWhiteSpace(req.FullName))
            await _userManager.AddClaimAsync(user, new Claim("FullName", req.FullName.Trim()));

        // Auto sign-in after register so the wizard can resume without a second round-trip.
        await _signInManager.SignInAsync(user, isPersistent: false);

        return Ok(await BuildStatusAsync(user));
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
    public async Task<ActionResult<AuthStatusDto>> Me()
    {
        var isAuth = User.Identity?.IsAuthenticated ?? false;
        if (!isAuth) return Ok(new AuthStatusDto());

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Ok(new AuthStatusDto());
        return Ok(await BuildStatusAsync(user));
    }

    private async Task<AuthStatusDto> BuildStatusAsync(IdentityUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? "";
        var claims = await _userManager.GetClaimsAsync(user);
        var fullName = claims.FirstOrDefault(c => c.Type == "FullName")?.Value ?? "";
        return new AuthStatusDto
        {
            IsAuthenticated = true,
            Email = user.Email ?? "",
            Role = role,
            FullName = fullName
        };
    }
}
