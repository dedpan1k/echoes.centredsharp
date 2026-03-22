/* SPDX-License-Identifier: ZLIB
Copyright (c) 2014 - 2023 Guillaume Vareille http://ysengrin.com
  _________
 /         \ tinyfiledialogsTest.cs v3.15.1 [Nov 19, 2023] zlib licence
 |tiny file| C# bindings created [2015]
 | dialogs |
 \____  ___/ http://tinyfiledialogs.sourceforge.net
      \|     git clone http://git.code.sf.net/p/tinyfiledialogs/code tinyfd
         ____________________________________________
        |                                            |
        |   email: tinyfiledialogs at ysengrin.com   |
        |____________________________________________|

If you like tinyfiledialogs, please upvote my stackoverflow answer
https://stackoverflow.com/a/47651444

- License -
 This software is provided 'as-is', without any express or implied
 warranty.  In no event will the authors be held liable for any damages
 arising from the use of this software.
 Permission is granted to anyone to use this software for any purpose,
 including commercial applications, and to alter it and redistribute it
 freely, subject to the following restrictions:
 1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software.  If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
 2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
 3. This notice may not be removed or altered from any source distribution.
*/

using System.Runtime.InteropServices;

namespace CentrED;

/// <summary>
/// Provides P/Invoke bindings and convenience helpers for the tinyfiledialogs library.
/// </summary>
public class TinyFileDialogs
{
    private const string LIB_NAME = "tinyfiledialogs";
    
    // Cross-platform UTF-8 entry points.
    [DllImport(LIB_NAME, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Plays the platform-default alert sound.
        /// </summary>
        public static extern void tinyfd_beep();

    [DllImport(LIB_NAME, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Shows a notification popup using the UTF-8 tinyfiledialogs entry point.
        /// </summary>
        public static extern int tinyfd_notifyPopup(string aTitle, string aMessage, string aIconType);
    [DllImport(LIB_NAME, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Shows a message box using the UTF-8 tinyfiledialogs entry point.
        /// </summary>
        public static extern int tinyfd_messageBox(string aTitle, string aMessage, string aDialogType, string aIconType, int aDefaultButton);
    [DllImport(LIB_NAME, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Shows an input box using the UTF-8 tinyfiledialogs entry point.
        /// </summary>
        public static extern IntPtr tinyfd_inputBox(string aTitle, string aMessage, string aDefaultInput);
    [DllImport(LIB_NAME, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Shows a save-file dialog using the UTF-8 tinyfiledialogs entry point.
        /// </summary>
        public static extern IntPtr tinyfd_saveFileDialog(string aTitle, string aDefaultPathAndFile, int aNumOfFilterPatterns, string[] aFilterPatterns, string aSingleFilterDescription);
    [DllImport(LIB_NAME, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Shows an open-file dialog using the UTF-8 tinyfiledialogs entry point.
        /// </summary>
        public static extern IntPtr tinyfd_openFileDialog(string aTitle, string aDefaultPathAndFile, int aNumOfFilterPatterns, string[] aFilterPatterns, string aSingleFilterDescription, int aAllowMultipleSelects);
    [DllImport(LIB_NAME, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr tinyfd_selectFolderDialog(string aTitle, string aDefaultPathAndFile);
    [DllImport(LIB_NAME, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Shows a color chooser using the UTF-8 tinyfiledialogs entry point.
        /// </summary>
        public static extern IntPtr tinyfd_colorChooser(string aTitle, string aDefaultHexRGB, byte[] aDefaultRGB, byte[] aoResultRGB);

    // Windows-only UTF-16 entry points.
    [DllImport(LIB_NAME, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Shows a notification popup using the UTF-16 tinyfiledialogs entry point.
        /// </summary>
        public static extern int tinyfd_notifyPopupW(string aTitle, string aMessage, string aIconType);
    [DllImport(LIB_NAME, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Shows a message box using the UTF-16 tinyfiledialogs entry point.
        /// </summary>
        public static extern int tinyfd_messageBoxW(string aTitle, string aMessage, string aDialogType, string aIconType, int aDefaultButton);
    [DllImport(LIB_NAME, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Shows an input box using the UTF-16 tinyfiledialogs entry point.
        /// </summary>
        public static extern IntPtr tinyfd_inputBoxW(string aTitle, string aMessage, string aDefaultInput);
    [DllImport(LIB_NAME, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Shows a save-file dialog using the UTF-16 tinyfiledialogs entry point.
        /// </summary>
        public static extern IntPtr tinyfd_saveFileDialogW(string aTitle, string aDefaultPathAndFile, int aNumOfFilterPatterns, string[] aFilterPatterns, string aSingleFilterDescription);
    [DllImport(LIB_NAME, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Shows an open-file dialog using the UTF-16 tinyfiledialogs entry point.
        /// </summary>
        public static extern IntPtr tinyfd_openFileDialogW(string aTitle, string aDefaultPathAndFile, int aNumOfFilterPatterns, string[] aFilterPatterns, string aSingleFilterDescription, int aAllowMultipleSelects);
    [DllImport(LIB_NAME, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Shows a folder picker using the UTF-16 tinyfiledialogs entry point.
        /// </summary>
        public static extern IntPtr tinyfd_selectFolderDialogW(string aTitle, string aDefaultPathAndFile);
    [DllImport(LIB_NAME, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Shows a color chooser using the UTF-16 tinyfiledialogs entry point.
        /// </summary>
        public static extern IntPtr tinyfd_colorChooserW(string aTitle, string aDefaultHexRGB, byte[] aDefaultRGB, byte[] aoResultRGB);

    // Cross-platform global tinyfiledialogs state accessors.
    [DllImport(LIB_NAME, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Reads a string-valued tinyfiledialogs global variable.
        /// </summary>
        public static extern IntPtr tinyfd_getGlobalChar(string aCharVariableName);
    [DllImport(LIB_NAME, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Reads an integer-valued tinyfiledialogs global variable.
        /// </summary>
        public static extern int tinyfd_getGlobalInt(string aIntVariableName);
    [DllImport(LIB_NAME, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// Writes an integer-valued tinyfiledialogs global variable.
        /// </summary>
        public static extern int tinyfd_setGlobalInt(string aIntVariableName, int aValue);
        
    private static string stringFromAnsi(IntPtr ptr) // for UTF-8/char
    {
        return System.Runtime.InteropServices.Marshal.PtrToStringAnsi(ptr);
    }

    private static string stringFromUni(IntPtr ptr) // for UTF-16/wchar_t
    {
        return System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr);
    }

    /// <summary>
    /// Opens a folder picker and returns the selected folder when the user confirms.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="defaultInput">The initial folder path.</param>
    /// <param name="result">The selected folder path when the call succeeds.</param>
    /// <returns><c>true</c> when the user selected a folder; otherwise, <c>false</c>.</returns>
    public static bool TrySelectFolder(string title, string defaultInput, out string result)
    {
        result = stringFromAnsi(tinyfd_selectFolderDialog(title, defaultInput));
        return !string.IsNullOrEmpty(result);
    }

    /// <summary>
    /// Opens a file picker and returns the selected path when the user confirms.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="defaultPathAndFile">The initial path or file selection.</param>
    /// <param name="filterPatterns">The file filter patterns.</param>
    /// <param name="singleFilterDescription">The description shown for the filter list.</param>
    /// <param name="allowMultipleSelects">Whether the dialog allows multiple file selections.</param>
    /// <param name="result">The selected file path or paths when the call succeeds.</param>
    /// <returns><c>true</c> when the user selected at least one file; otherwise, <c>false</c>.</returns>
    public static bool TryOpenFile(string title, string defaultPathAndFile, string[] filterPatterns, string singleFilterDescription, bool allowMultipleSelects, out string result)
    {
        result = stringFromAnsi(tinyfd_openFileDialog(title, defaultPathAndFile, filterPatterns.Length, filterPatterns, singleFilterDescription, allowMultipleSelects ? 1 : 0));
        return !string.IsNullOrEmpty(result);
    }
    
    /// <summary>
    /// Opens a save-file dialog and returns the chosen path when the user confirms.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="defaultPathAndFile">The initial path or file name.</param>
    /// <param name="filterPatterns">The file filter patterns.</param>
    /// <param name="singleFilterDescription">The description shown for the filter list.</param>
    /// <param name="result">The chosen save path when the call succeeds.</param>
    /// <returns><c>true</c> when the user selected a save path; otherwise, <c>false</c>.</returns>
    public static bool TrySaveFile(string title, string defaultPathAndFile, string[] filterPatterns, string singleFilterDescription, out string result)
    {
        result = stringFromAnsi(tinyfd_saveFileDialog(title, defaultPathAndFile, filterPatterns.Length, filterPatterns, singleFilterDescription));
        return !string.IsNullOrEmpty(result);
    }
}