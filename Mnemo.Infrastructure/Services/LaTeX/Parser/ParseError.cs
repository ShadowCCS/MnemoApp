using System;

namespace Mnemo.Infrastructure.Services.LaTeX.Parser;

public class ParseError : Exception
{
    public int Position { get; }
    
    public ParseError(string message, int position) : base(message)
    {
        Position = position;
    }
}