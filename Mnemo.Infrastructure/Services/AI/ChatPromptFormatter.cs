namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Formats system + user text for local chat templates (ChatML, Llama3, etc.).
/// </summary>
public static class ChatPromptFormatter
{
    public static string Format(string template, string system, string user)
    {
        return template.ToUpperInvariant() switch
        {
            "CHATML" => $"""
                <|im_start|>system
                {system}<|im_end|>
                <|im_start|>user
                {user}<|im_end|>
                <|im_start|>assistant

                """,
            "LLAMA3" => $"""
                <|begin_of_text|><|start_header_id|>system<|end_header_id|>

                {system}<|eot_id|><|start_header_id|>user<|end_header_id|>

                {user}<|eot_id|><|start_header_id|>assistant<|end_header_id|>

                """,
            "ALPACA" => $"""
                ### Instruction:
                {system}

                {user}

                ### Response:
                """,
            "VICUNA" => $"""
                {system}

                USER: {user}
                ASSISTANT:
                """,
            _ => $"""
                System: {system}
                User: {user}
                Assistant:
                """
        };
    }
}
