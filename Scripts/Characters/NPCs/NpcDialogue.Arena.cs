// NpcDialogue: ending the conversation from outside (the Ronin Arena starts a duel right after its window)
public partial class NpcDialogue
{
    public static void EndConversation()
    {
        if (Instance != null && Instance.mode != Mode.None) Instance.End(false);
    }
}
