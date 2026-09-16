namespace Bugtopia.Launcher
{
    /// <summary>
    /// What <see cref="Api"/> needs from whichever window hosts it: the Photino page
    /// (<c>PhotinoHost</c>) or the native one (<c>Win32Host</c>, <c>-p:BugtopiaUi=win32</c>).
    /// </summary>
    internal interface IDialogs
    {
        string PickFolder(string title, string current);
        string PickFile(string title, string filterName, string[] extensions);

        /// <summary>Resizes the window, for the switch between the simple and expert views.</summary>
        void Resize(int width, int height);

        /// <summary>Puts the window on screen, once the page has drawn itself.</summary>
        void Reveal();

        /// <summary>Closes the launcher, once the game is running and injected.</summary>
        void Close();

        /// <summary>Puts text on the clipboard, owned by the launcher's window. False when it could not.</summary>
        bool CopyText(string text);
    }
}
