﻿using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using UnityObject = UnityEngine.Object;
using Il2CppType = Il2CppSystem.Type;
using Il2CppFieldInfo = Il2CppSystem.Reflection.FieldInfo;
using Il2CppMethodInfo = Il2CppSystem.Reflection.MethodInfo;
using Il2CppPropertyInfo = Il2CppSystem.Reflection.PropertyInfo;
using Il2CppBindingFlags = Il2CppSystem.Reflection.BindingFlags;
using Il2CppObject = Il2CppSystem.Object;
using Object = UnityEngine.Object;


namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private void SendEscMessage()
        {
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd != IntPtr.Zero)
                {
                    PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_ESCAPE, IntPtr.Zero);
                    PostMessage(hWnd, WM_KEYUP, (IntPtr)VK_ESCAPE, IntPtr.Zero);
                }
            }
            catch { }
        }

        private void SendEnterMessage()
        {
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd != IntPtr.Zero)
                {
                    PostMessage(hWnd, WM_KEYDOWN, (IntPtr)0x0D, IntPtr.Zero);
                    PostMessage(hWnd, WM_KEYUP, (IntPtr)0x0D, IntPtr.Zero);
                }
            }
            catch { }
        }

        private void SendFMessage()
        {
            // Prefer using simulated F-key flags (safer for in-game automation); fallback to native input if needed
            try
            {
                this.SimulateFKeyPulse(0.12f);
                return;
            }
            catch { }
            try
            {
                // Use SendInput for better compatibility if simulation fails
                INPUT[] inputs = new INPUT[2];
                inputs[0].type = 1; // INPUT_KEYBOARD
                inputs[0].u.ki.wVk = VK_F;
                inputs[0].u.ki.dwFlags = 0; // KEYEVENTF_KEYDOWN

                inputs[1].type = 1; // INPUT_KEYBOARD
                inputs[1].u.ki.wVk = VK_F;
                inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;

                SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
            }
            catch { }
        }

        private void SimulateFKeyPulse(float holdSeconds = 0.12f)
        {
            try
            {
                this.EnsureInputSimPatched();
                HeartopiaComplete.SimulateFKeyDown = true;
                HeartopiaComplete.SimulateFKeyHeld = true;
                HeartopiaComplete.SimulateFKeyUp = false;
                this.nextSimulatedFKeyClearAt = Time.unscaledTime + Mathf.Max(0.02f, holdSeconds);
            }
            catch { }
        }

        private void SendLeftClickMessage()
        {
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd != IntPtr.Zero)
                {
                    PostMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, IntPtr.Zero);
                    PostMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch { }
        }

        private void SendLeftClickInputTap()
        {
            try
            {
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            }
            catch
            {
                this.SendLeftClickMessage();
            }
        }

    }
}
