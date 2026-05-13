using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MaisonGlace.API.Models;
using MaisonGlace.API.Services;

namespace MaisonGlace.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookingsController : ControllerBase
{
    private readonly BookingService _bookings;
    private readonly EmailService _email;
    private readonly ILogger<BookingsController> _logger;

    public BookingsController(BookingService bookings, EmailService email,
        ILogger<BookingsController> logger)
    {
        _bookings = bookings;
        _email = email;
        _logger = logger;
    }

    // ── Public: create booking ────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Booking booking)
    {
        if (string.IsNullOrWhiteSpace(booking.Name)
            || string.IsNullOrWhiteSpace(booking.Email)
            || string.IsNullOrWhiteSpace(booking.Date)
            || string.IsNullOrWhiteSpace(booking.Time))
        {
            return BadRequest(new { message = "Please fill in all required fields." });
        }

        var created = await _bookings.CreateAsync(booking);

        // Do not fire-and-forget: run email attempts now so failures are visible in logs.
        await SendEmailsAsync(created);

        return Ok(new { success = true, booking = created });
    }

    // ── Admin: read ───────────────────────────────────────────────────────

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _bookings.GetAllAsync());

    [Authorize]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var booking = await _bookings.GetByIdAsync(id);
        return booking is null ? NotFound() : Ok(booking);
    }

    // ── Admin: update ─────────────────────────────────────────────────────

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] Booking booking)
    {
        var success = await _bookings.UpdateAsync(id, booking);
        return success ? Ok(new { success = true }) : NotFound();
    }

    // ── Admin: soft delete ────────────────────────────────────────────────

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> SoftDelete(string id)
    {
        var success = await _bookings.SoftDeleteAsync(id);
        return success ? Ok(new { success = true }) : NotFound();
    }

    // ── Admin: add receipt item ───────────────────────────────────────────

    [Authorize]
    [HttpPost("{id}/receipt")]
    public async Task<IActionResult> AddReceiptItem(string id, [FromBody] ReceiptItem item)
    {
        var success = await _bookings.AddReceiptItemAsync(id, item);
        return success ? Ok(new { success = true }) : NotFound();
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private async Task SendEmailsAsync(Booking booking)
    {
        _logger.LogInformation("Booking email dispatch started for {Ref} to guest {Email}", booking.ReferenceNumber, booking.Email);

        try { await _email.SendBookingConfirmationAsync(booking); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Confirmation email failed for {Ref}", booking.ReferenceNumber);
        }

        try { await _email.SendAdminNotificationAsync(booking); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin notification failed for {Ref}", booking.ReferenceNumber);
        }

        _logger.LogInformation("Booking email dispatch completed for {Ref}", booking.ReferenceNumber);
    }
}
