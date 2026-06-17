namespace Astral.Core.Discord;

public interface IDiscordProcessInspector
{
    DiscordProcessSnapshot Capture();
}
