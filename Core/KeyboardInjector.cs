using System.Runtime.InteropServices;
using KerkenezSpeech.Models;
using KerkenezSpeech.Services;

namespace KerkenezSpeech.Core;

public class KeyboardInjector
{
    private readonly ConfigService _configService;
    private string _currentUtteranceText = string.Empty;
    private readonly object _lock = new();

    public KeyboardInjector(ConfigService configService)
    {
        _configService = configService;
    }

    public void ProcessStreamingText(string newText)
    {
        if (string.IsNullOrEmpty(newText)) return;

        lock (_lock)
        {
            if (_configService.Config.TypingMode == TypingMode.SentenceBySentence)
            {
                _currentUtteranceText = newText;
                return;
            }

            string previous = _currentUtteranceText;
            if (previous == newText) return;

            int commonLength = 0;
            int minLen = Math.Min(previous.Length, newText.Length);
            while (commonLength < minLen && previous[commonLength] == newText[commonLength])
            {
                commonLength++;
            }

            int backspacesNeeded = previous.Length - commonLength;
            if (backspacesNeeded > 0)
            {
                SendBackspaces(backspacesNeeded);
            }

            string toAppend = newText[commonLength..];
            if (!string.IsNullOrEmpty(toAppend))
            {
                SendUnicodeString(toAppend);
            }

            _currentUtteranceText = newText;
        }
    }

    public void FinalizeUtterance(string? finalText = null)
    {
        lock (_lock)
        {
            if (_configService.Config.TypingMode == TypingMode.SentenceBySentence)
            {
                string textToType = !string.IsNullOrWhiteSpace(finalText) ? finalText : _currentUtteranceText;
                if (!string.IsNullOrWhiteSpace(textToType))
                {
                    SendUnicodeString(textToType);
                    if (_configService.Config.AddTrailingSpace && !textToType.EndsWith(" "))
                    {
                        SendUnicodeString(" ");
                    }
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(_currentUtteranceText))
                {
                    if (_configService.Config.AddTrailingSpace && !_currentUtteranceText.EndsWith(" "))
                    {
                        SendUnicodeString(" ");
                    }
                }
            }

            _currentUtteranceText = string.Empty;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _currentUtteranceText = string.Empty;
        }
    }

    public static void SendUnicodeString(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        List<NativeWin32.INPUT> inputs = new(text.Length * 2);

        foreach (char c in text)
        {
            if (c == '\r') continue;

            if (c == '\n')
            {
                inputs.Add(new NativeWin32.INPUT
                {
                    type = NativeWin32.INPUT_KEYBOARD,
                    u = new NativeWin32.InputUnion
                    {
                        ki = new NativeWin32.KEYBDINPUT
                        {
                            wVk = NativeWin32.VK_RETURN,
                            wScan = 0,
                            dwFlags = 0,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                });
                inputs.Add(new NativeWin32.INPUT
                {
                    type = NativeWin32.INPUT_KEYBOARD,
                    u = new NativeWin32.InputUnion
                    {
                        ki = new NativeWin32.KEYBDINPUT
                        {
                            wVk = NativeWin32.VK_RETURN,
                            wScan = 0,
                            dwFlags = NativeWin32.KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                });
            }
            else
            {
                inputs.Add(new NativeWin32.INPUT
                {
                    type = NativeWin32.INPUT_KEYBOARD,
                    u = new NativeWin32.InputUnion
                    {
                        ki = new NativeWin32.KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = c,
                            dwFlags = NativeWin32.KEYEVENTF_UNICODE,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                });
                inputs.Add(new NativeWin32.INPUT
                {
                    type = NativeWin32.INPUT_KEYBOARD,
                    u = new NativeWin32.InputUnion
                    {
                        ki = new NativeWin32.KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = c,
                            dwFlags = NativeWin32.KEYEVENTF_UNICODE | NativeWin32.KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                });
            }
        }

        if (inputs.Count > 0)
        {
            var arr = inputs.ToArray();
            NativeWin32.SendInput((uint)arr.Length, arr, Marshal.SizeOf<NativeWin32.INPUT>());
        }
    }

    public static void SendBackspaces(int count)
    {
        if (count <= 0) return;

        NativeWin32.INPUT[] inputs = new NativeWin32.INPUT[count * 2];
        for (int i = 0; i < count; i++)
        {
            inputs[i * 2] = new NativeWin32.INPUT
            {
                type = NativeWin32.INPUT_KEYBOARD,
                u = new NativeWin32.InputUnion
                {
                    ki = new NativeWin32.KEYBDINPUT
                    {
                        wVk = NativeWin32.VK_BACK,
                        wScan = 0,
                        dwFlags = 0,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            inputs[i * 2 + 1] = new NativeWin32.INPUT
            {
                type = NativeWin32.INPUT_KEYBOARD,
                u = new NativeWin32.InputUnion
                {
                    ki = new NativeWin32.KEYBDINPUT
                    {
                        wVk = NativeWin32.VK_BACK,
                        wScan = 0,
                        dwFlags = NativeWin32.KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        NativeWin32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeWin32.INPUT>());
    }
}
