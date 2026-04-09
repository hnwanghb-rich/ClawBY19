using System.ComponentModel.DataAnnotations;

namespace ClawBY19.Data.Entities;

public class ChatSession
{
    [Key] public int Id { get; set; }
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "新会话";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsActive { get; set; } = true;
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}

public class ChatMessage
{
    [Key] public int Id { get; set; }
    public int SessionId { get; set; }
    public ChatSession Session { get; set; } = null!;
    public string Role { get; set; } = "user";        // user | assistant | system
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string? ModelUsed { get; set; }
    public int TokensInput { get; set; }
    public int TokensOutput { get; set; }
    public decimal CostCny { get; set; }
}
