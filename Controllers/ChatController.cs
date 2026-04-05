using Microsoft.AspNetCore.Mvc;
using SqlChatbot.Services;

namespace SqlChatbot.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly SqlChatService _chatService;

    public ChatController(SqlChatService chatService) => _chatService = chatService;

    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest("Prompt cannot be empty.");

        var answer = await _chatService.AskAsync(request.Prompt);
        return Ok(new { answer });
    }
}

public record ChatRequest(string Prompt);