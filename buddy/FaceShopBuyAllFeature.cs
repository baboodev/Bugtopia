using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const int FaceShopBuyAllShopId = 3008;
        private const int FaceShopBuyAllCoinCurrency = 1;
        private const int FaceShopBuyAllUnlockStatus = 1;

        private struct FaceShopBuyAllCandidate
        {
            public string Kind;
            public int StyleId;
            public int Price;
            public int FacePartType;
        }

        private struct FaceShopRuntime
        {
            public bool UseAura;
            public object ManagedFaceSystem;
            public Type ManagedFaceSystemType;
            public Type ManagedTableDataType;
            public IntPtr AuraFaceSystem;
            public IntPtr AuraFaceSystemClass;
            public IntPtr AuraTableDataClass;
            public IntPtr AuraAvatarFaceValueClass;
        }

        private AuraMonoObjectCache faceShopAuraFaceSystemObj;
        private IntPtr faceShopAuraFaceSystemClass = IntPtr.Zero;
        private IntPtr faceShopAuraRefreshShopDataMethod = IntPtr.Zero;
        private IntPtr faceShopAuraGetCurrentAvatarMethod = IntPtr.Zero;
        private IntPtr faceShopAuraSaveFaceDataMethod = IntPtr.Zero;
        private IntPtr faceShopAuraAvatarFaceValueClass = IntPtr.Zero;

        private void StartFaceShopBuyAllCoin()
        {
            if (this.shopBuyAllRunning || this.shopBuyAllCoroutine != null)
            {
                this.AddMenuNotification(this.L("Shop buy-all already running"), new Color(0.45f, 0.88f, 1f));
                return;
            }

            this.shopBuyAllStatus = "Preparing Face Shop (id " + FaceShopBuyAllShopId + ")...";
            this.shopBuyAllRunning = true;
            this.shopBuyAllCoroutine = ModCoroutines.Start(this.FaceShopBuyAllCoinRoutine());
        }

        private bool TryResolveFaceShopRuntime(out FaceShopRuntime runtime, out string error)
        {
            runtime = default;
            error = null;

            Type faceSystemType = this.FindLoadedType(
                "XDTGameSystem.GameplaySystem.DressingUp.AvatarFaceSystem",
                "AvatarFaceSystem");
            if (faceSystemType != null)
            {
                object faceSystem = null;
                PropertyInfo instanceProperty = this.GetDataModuleInstanceProperty(faceSystemType);
                if (instanceProperty != null)
                {
                    faceSystem = instanceProperty.GetValue(null, null);
                }

                if (faceSystem == null)
                {
                    this.TryGetManagedModule(faceSystemType, out faceSystem);
                }

                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (faceSystem != null && tableDataType != null)
                {
                    runtime.UseAura = false;
                    runtime.ManagedFaceSystem = faceSystem;
                    runtime.ManagedFaceSystemType = faceSystemType;
                    runtime.ManagedTableDataType = tableDataType;
                    return true;
                }
            }

            if (!this.TryEnsureFaceShopAuraRuntime(out error))
            {
                error = error ?? "AvatarFaceSystem unavailable (enter town).";
                return false;
            }

            runtime.UseAura = true;
            // Raw copy is safe while the pinned field cache below keeps the object rooted.
            this.faceShopAuraFaceSystemObj.TryGet(out IntPtr auraFaceSystemObj);
            runtime.AuraFaceSystem = auraFaceSystemObj;
            runtime.AuraFaceSystemClass = this.faceShopAuraFaceSystemClass;
            runtime.AuraTableDataClass = this.FindAuraMonoTableDataClass();
            runtime.AuraAvatarFaceValueClass = this.faceShopAuraAvatarFaceValueClass;
            if (runtime.AuraTableDataClass == IntPtr.Zero)
            {
                error = "Aura TableData missing.";
                return false;
            }

            return true;
        }

        private bool TryEnsureFaceShopAuraRuntime(out string error)
        {
            error = null;
            if (this.faceShopAuraFaceSystemObj.TryGet(out _)
                && this.faceShopAuraRefreshShopDataMethod != IntPtr.Zero
                && this.faceShopAuraGetCurrentAvatarMethod != IntPtr.Zero
                && this.faceShopAuraSaveFaceDataMethod != IntPtr.Zero
                && this.faceShopAuraAvatarFaceValueClass != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                error = "AuraMono unavailable.";
                return false;
            }

            if (this.faceShopAuraFaceSystemClass == IntPtr.Zero)
            {
                this.faceShopAuraFaceSystemClass = this.FindAuraMonoClassByFullName("XDTGameSystem.GameplaySystem.DressingUp.AvatarFaceSystem");
            }

            if (this.faceShopAuraFaceSystemClass == IntPtr.Zero)
            {
                error = "Aura AvatarFaceSystem class missing.";
                return false;
            }

            if (!this.faceShopAuraFaceSystemObj.TryGet(out _))
            {
                IntPtr faceSystemObj = this.TryGetAuraMonoDataModuleInstance(this.faceShopAuraFaceSystemClass);
                if (faceSystemObj == IntPtr.Zero
                    && !this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.DressingUp.AvatarFaceSystem", out faceSystemObj))
                {
                    error = "Aura AvatarFaceSystem instance missing.";
                    return false;
                }
                this.faceShopAuraFaceSystemObj.Set(faceSystemObj);
            }

            this.faceShopAuraRefreshShopDataMethod = this.FindAuraMonoMethodOnHierarchy(this.faceShopAuraFaceSystemClass, "RefreshShopData", 1);
            this.faceShopAuraGetCurrentAvatarMethod = this.FindAuraMonoMethodOnHierarchy(this.faceShopAuraFaceSystemClass, "GetCurrentAvatar", 0);
            this.faceShopAuraSaveFaceDataMethod = this.FindAuraMonoMethodOnHierarchy(this.faceShopAuraFaceSystemClass, "SaveFaceData", 2);
            this.faceShopAuraAvatarFaceValueClass = this.FindAuraMonoClassByFullName("XDTGameSystem.GameplaySystem.DressingUp.AvatarFaceValue");
            if (this.faceShopAuraAvatarFaceValueClass == IntPtr.Zero)
            {
                error = "Aura AvatarFaceValue class missing.";
                return false;
            }

            if (this.faceShopAuraRefreshShopDataMethod == IntPtr.Zero
                || this.faceShopAuraGetCurrentAvatarMethod == IntPtr.Zero
                || this.faceShopAuraSaveFaceDataMethod == IntPtr.Zero)
            {
                error = "Aura AvatarFaceSystem methods missing.";
                return false;
            }

            return true;
        }

        private unsafe bool TryInvokeFaceShopAuraRefreshShopData(in FaceShopRuntime runtime, int shopId, out string error)
        {
            error = null;
            if (!runtime.UseAura || auraMonoRuntimeInvoke == null)
            {
                error = "Aura runtime unavailable.";
                return false;
            }

            int shopIdValue = shopId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&shopIdValue);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.faceShopAuraRefreshShopDataMethod, runtime.AuraFaceSystem, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                error = "Aura RefreshShopData failed.";
                return false;
            }

            return true;
        }

        private unsafe bool TryInvokeFaceShopAuraGetCurrent(in FaceShopRuntime runtime, out IntPtr faceValue, out string error)
        {
            faceValue = IntPtr.Zero;
            error = null;
            if (!runtime.UseAura || auraMonoRuntimeInvoke == null)
            {
                error = "Aura runtime unavailable.";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            faceValue = auraMonoRuntimeInvoke(this.faceShopAuraGetCurrentAvatarMethod, runtime.AuraFaceSystem, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || faceValue == IntPtr.Zero)
            {
                error = "Aura GetCurrentAvatar failed.";
                return false;
            }

            return true;
        }

        private unsafe bool TryInvokeFaceShopAuraSaveData(in FaceShopRuntime runtime, IntPtr faceValue, bool needIgnoreApply, out string error)
        {
            error = null;
            if (!runtime.UseAura || faceValue == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                error = "Aura save unavailable.";
                return false;
            }

            bool ignoreValue = needIgnoreApply;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = faceValue;
            args[1] = (IntPtr)(&ignoreValue);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.faceShopAuraSaveFaceDataMethod, runtime.AuraFaceSystem, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                error = "Aura SaveFaceData failed.";
                return false;
            }

            return true;
        }

        private unsafe IntPtr TryCreateAuraAvatarFaceValueCopy(IntPtr source, in FaceShopRuntime runtime, out string error)
        {
            error = null;
            if (source == IntPtr.Zero || runtime.AuraAvatarFaceValueClass == IntPtr.Zero || auraMonoObjectNew == null || auraMonoRuntimeInvoke == null)
            {
                error = "Aura face value unavailable.";
                return IntPtr.Zero;
            }

            IntPtr copyObj = auraMonoObjectNew(this.auraMonoRootDomain, runtime.AuraAvatarFaceValueClass);
            if (copyObj == IntPtr.Zero)
            {
                error = "Aura AvatarFaceValue alloc failed.";
                return IntPtr.Zero;
            }

            if (auraMonoRuntimeObjectInit != null)
            {
                auraMonoRuntimeObjectInit(copyObj);
            }

            IntPtr copyMethod = this.FindAuraMonoMethodOnHierarchy(runtime.AuraAvatarFaceValueClass, "Copy", 1);
            if (copyMethod == IntPtr.Zero)
            {
                error = "Aura AvatarFaceValue.Copy missing.";
                return IntPtr.Zero;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = source;
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(copyMethod, copyObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                error = "Aura AvatarFaceValue.Copy failed.";
                return IntPtr.Zero;
            }

            return copyObj;
        }

        private unsafe void TrySetAuraFaceValueIntField(IntPtr faceValue, string fieldName, int value)
        {
            if (faceValue == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoFieldSetValue == null)
            {
                return;
            }

            IntPtr klass = auraMonoObjectGetClass(faceValue);
            IntPtr field = klass != IntPtr.Zero ? this.FindAuraMonoFieldOnHierarchy(klass, fieldName) : IntPtr.Zero;
            if (field == IntPtr.Zero)
            {
                return;
            }

            int fieldValue = value;
            auraMonoFieldSetValue(faceValue, field, (IntPtr)(&fieldValue));
        }

        private int TryGetAuraFaceValueIntField(IntPtr faceValue, string fieldName)
        {
            return this.TryReadAuraMonoStructIntField(faceValue, fieldName);
        }

        private unsafe bool TryReadAuraTableCoinPrice(IntPtr tableDataClass, string methodName, int id, out int price, out int currency)
        {
            price = 0;
            currency = 0;
            if (tableDataClass == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr getter = this.FindAuraMonoMethodOnHierarchy(tableDataClass, methodName, 1);
            if (getter == IntPtr.Zero)
            {
                return false;
            }

            int idValue = id;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&idValue);
            IntPtr exc = IntPtr.Zero;
            IntPtr row = auraMonoRuntimeInvoke(getter, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || row == IntPtr.Zero)
            {
                return false;
            }

            price = this.TryReadAuraMonoStructIntField(row, "price");
            currency = this.TryReadAuraMonoStructIntField(row, "moneyValue");
            return true;
        }

        private unsafe int TryInvokeAuraRowValidColor(IntPtr rowObj, string methodName, int colorId)
        {
            if (rowObj == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return colorId;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(rowObj), methodName, 1);
            if (method == IntPtr.Zero)
            {
                return colorId;
            }

            int colorValue = colorId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&colorValue);
            IntPtr exc = IntPtr.Zero;
            IntPtr result = auraMonoRuntimeInvoke(method, rowObj, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero && result != IntPtr.Zero && this.TryUnboxMonoInt32(result, out int valid)
                ? valid
                : colorId;
        }

        private bool TryReadAuraPartDataUnlock(IntPtr partData, out int styleId)
        {
            styleId = 0;
            if (partData == IntPtr.Zero)
            {
                return false;
            }

            int status = this.TryReadAuraMonoStructIntField(partData, "status");
            styleId = this.TryReadAuraMonoStructIntField(partData, "styleId");
            return status == FaceShopBuyAllUnlockStatus;
        }

        private bool TryReadAuraColorDataUnlock(IntPtr colorData, out int colorId)
        {
            colorId = 0;
            if (colorData == IntPtr.Zero)
            {
                return false;
            }

            int status = this.TryReadAuraMonoStructIntField(colorData, "status");
            colorId = this.TryReadAuraMonoStructIntField(colorData, "colorId");
            return status == FaceShopBuyAllUnlockStatus;
        }

        private void TryCollectFaceShopAuraPartDictionary(
            IntPtr dictionaryObj,
            string kind,
            string tableMethod,
            IntPtr tableDataClass,
            int facePartType,
            List<FaceShopBuyAllCandidate> items)
        {
            if (dictionaryObj == IntPtr.Zero)
            {
                return;
            }

            List<IntPtr> entries = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(dictionaryObj, entries))
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                IntPtr partData = this.TryGetMonoDictionaryEntryValue(entries[i]);
                if (partData == IntPtr.Zero)
                {
                    partData = entries[i];
                }

                if (!this.TryReadAuraPartDataUnlock(partData, out int styleId))
                {
                    continue;
                }

                if (!this.TryReadAuraTableCoinPrice(tableDataClass, tableMethod, styleId, out int price, out int currency)
                    || currency != FaceShopBuyAllCoinCurrency
                    || price <= 0)
                {
                    continue;
                }

                items.Add(new FaceShopBuyAllCandidate
                {
                    Kind = kind,
                    StyleId = styleId,
                    Price = price,
                    FacePartType = facePartType
                });
            }
        }

        private bool TryCollectFaceShopCoinItemsAura(in FaceShopRuntime runtime, List<FaceShopBuyAllCandidate> items, out string error)
        {
            error = null;
            items.Clear();
            if (!runtime.UseAura)
            {
                error = "Aura runtime unavailable.";
                return false;
            }

            if (!this.TryInvokeFaceShopAuraRefreshShopData(in runtime, FaceShopBuyAllShopId, out error))
            {
                return false;
            }

            IntPtr getFaceDataResult = this.FindAuraMonoMethodOnHierarchy(runtime.AuraFaceSystemClass, "get_FaceDataResult", 0);
            IntPtr getColorDataResult = this.FindAuraMonoMethodOnHierarchy(runtime.AuraFaceSystemClass, "get_ColorDataResult", 0);
            if (getFaceDataResult == IntPtr.Zero || getColorDataResult == IntPtr.Zero)
            {
                error = "Aura face shop properties missing.";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr faceDataResult = auraMonoRuntimeInvoke(getFaceDataResult, runtime.AuraFaceSystem, IntPtr.Zero, ref exc);
            exc = IntPtr.Zero;
            IntPtr colorDataResult = auraMonoRuntimeInvoke(getColorDataResult, runtime.AuraFaceSystem, IntPtr.Zero, ref exc);
            if (faceDataResult == IntPtr.Zero || colorDataResult == IntPtr.Zero)
            {
                error = "Aura face shop data empty.";
                return false;
            }

            if (!this.TryGetMonoObjectMember(faceDataResult, "hairData", out IntPtr hairData)
                || !this.TryGetMonoObjectMember(faceDataResult, "beardData", out IntPtr beardData)
                || !this.TryGetMonoObjectMember(faceDataResult, "faceData", out IntPtr faceData))
            {
                error = "Aura FaceData fields missing.";
                return false;
            }

            this.TryCollectFaceShopAuraPartDictionary(hairData, "hair", "GetHair", runtime.AuraTableDataClass, 0, items);
            this.TryCollectFaceShopAuraPartDictionary(beardData, "beard", "GetBeard", runtime.AuraTableDataClass, 0, items);

            List<IntPtr> facePartEntries = new List<IntPtr>();
            if (this.TryEnumerateAuraMonoCollectionItems(faceData, facePartEntries))
            {
                for (int i = 0; i < facePartEntries.Count; i++)
                {
                    int facePartType = 0;
                    if (this.TryGetMonoObjectMember(facePartEntries[i], "Key", out IntPtr keyObj) && keyObj != IntPtr.Zero)
                    {
                        if (!this.TryUnboxMonoInt32(keyObj, out facePartType))
                        {
                            facePartType = this.TryReadAuraMonoStructIntField(keyObj, "value__");
                        }
                    }

                    IntPtr styleDictionary = this.TryGetMonoDictionaryEntryValue(facePartEntries[i]);
                    if (styleDictionary == IntPtr.Zero && this.TryGetMonoObjectMember(facePartEntries[i], "Value", out IntPtr valueObj))
                    {
                        styleDictionary = valueObj;
                    }

                    this.TryCollectFaceShopAuraPartDictionary(styleDictionary, "face", "GetFace", runtime.AuraTableDataClass, facePartType, items);
                }
            }

            if (this.TryGetMonoObjectMember(faceDataResult, "skinData", out IntPtr skinData))
            {
                List<IntPtr> skinIds = new List<IntPtr>();
                if (this.TryEnumerateAuraMonoCollectionItems(skinData, skinIds))
                {
                    for (int i = 0; i < skinIds.Count; i++)
                    {
                        int skinId = 0;
                        if (!this.TryUnboxMonoInt32(skinIds[i], out skinId))
                        {
                            skinId = this.TryReadAuraMonoStructIntField(skinIds[i], "m_value");
                        }

                        if (skinId <= 0)
                        {
                            continue;
                        }

                        List<IntPtr> colorEntries = new List<IntPtr>();
                        if (!this.TryEnumerateAuraMonoCollectionItems(colorDataResult, colorEntries))
                        {
                            continue;
                        }

                        for (int c = 0; c < colorEntries.Count; c++)
                        {
                            IntPtr colorData = this.TryGetMonoDictionaryEntryValue(colorEntries[c]);
                            if (colorData == IntPtr.Zero)
                            {
                                colorData = colorEntries[c];
                            }

                            if (!this.TryReadAuraColorDataUnlock(colorData, out int colorId) || colorId != skinId)
                            {
                                continue;
                            }

                            if (!this.TryReadAuraTableCoinPrice(runtime.AuraTableDataClass, "GetAvatarcolors", colorId, out int price, out int currency)
                                || currency != FaceShopBuyAllCoinCurrency
                                || price <= 0)
                            {
                                continue;
                            }

                            items.Add(new FaceShopBuyAllCandidate
                            {
                                Kind = "skin",
                                StyleId = colorId,
                                Price = price,
                                FacePartType = 1
                            });
                            break;
                        }
                    }
                }
            }

            if (items.Count <= 0)
            {
                error = "No purchasable Coin face shop items.";
                return false;
            }

            return true;
        }

        private unsafe bool TryApplyFaceShopCandidateAura(
            IntPtr baselineFace,
            in FaceShopRuntime runtime,
            in FaceShopBuyAllCandidate candidate,
            out IntPtr purchaseFace,
            out string error)
        {
            purchaseFace = IntPtr.Zero;
            error = null;
            purchaseFace = this.TryCreateAuraAvatarFaceValueCopy(baselineFace, in runtime, out error);
            if (purchaseFace == IntPtr.Zero)
            {
                return false;
            }

            if (string.Equals(candidate.Kind, "skin", StringComparison.Ordinal))
            {
                this.TrySetAuraFaceValueIntField(purchaseFace, "SkinColor", candidate.StyleId);
                return true;
            }

            if (string.Equals(candidate.Kind, "hair", StringComparison.Ordinal))
            {
                int idValue = candidate.StyleId;
                IntPtr getHair = this.FindAuraMonoMethodOnHierarchy(runtime.AuraTableDataClass, "GetHair", 1);
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&idValue);
                IntPtr exc = IntPtr.Zero;
                IntPtr hairRow = auraMonoRuntimeInvoke(getHair, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || hairRow == IntPtr.Zero)
                {
                    error = "hair row missing.";
                    return false;
                }

                int mainColor = this.TryInvokeAuraRowValidColor(hairRow, "GetValidMainColor", this.TryGetAuraFaceValueIntField(baselineFace, "HairColorMain"));
                int subColor = this.TryInvokeAuraRowValidColor(hairRow, "GetValidSubColor", this.TryGetAuraFaceValueIntField(baselineFace, "HairColorSub"));
                this.TrySetAuraFaceValueIntField(purchaseFace, "HairStyle", candidate.StyleId);
                this.TrySetAuraFaceValueIntField(purchaseFace, "HairColorMain", mainColor);
                this.TrySetAuraFaceValueIntField(purchaseFace, "HairColorSub", subColor);
                return true;
            }

            if (string.Equals(candidate.Kind, "beard", StringComparison.Ordinal))
            {
                int idValue = candidate.StyleId;
                IntPtr getBeard = this.FindAuraMonoMethodOnHierarchy(runtime.AuraTableDataClass, "GetBeard", 1);
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&idValue);
                IntPtr exc = IntPtr.Zero;
                IntPtr beardRow = auraMonoRuntimeInvoke(getBeard, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || beardRow == IntPtr.Zero)
                {
                    error = "beard row missing.";
                    return false;
                }

                int mainColor = this.TryInvokeAuraRowValidColor(beardRow, "GetValidMainColor", this.TryGetAuraFaceValueIntField(baselineFace, "BeardColorMain"));
                int subColor = this.TryInvokeAuraRowValidColor(beardRow, "GetValidSubColor", this.TryGetAuraFaceValueIntField(baselineFace, "BeardColorSub"));
                this.TrySetAuraFaceValueIntField(purchaseFace, "BeardStyle", candidate.StyleId);
                this.TrySetAuraFaceValueIntField(purchaseFace, "BeardColorMain", mainColor);
                this.TrySetAuraFaceValueIntField(purchaseFace, "BeardColorSub", subColor);
                return true;
            }

            if (!string.Equals(candidate.Kind, "face", StringComparison.Ordinal))
            {
                error = "unknown candidate kind.";
                return false;
            }

            int faceId = candidate.StyleId;
            IntPtr getFace = this.FindAuraMonoMethodOnHierarchy(runtime.AuraTableDataClass, "GetFace", 1);
            IntPtr* faceArgs = stackalloc IntPtr[1];
            faceArgs[0] = (IntPtr)(&faceId);
            IntPtr faceExc = IntPtr.Zero;
            IntPtr faceRow = auraMonoRuntimeInvoke(getFace, IntPtr.Zero, (IntPtr)faceArgs, ref faceExc);
            if (faceExc != IntPtr.Zero || faceRow == IntPtr.Zero)
            {
                error = "face row missing.";
                return false;
            }

            int defaultColor = this.TryReadAuraMonoStructIntField(faceRow, "defaultColor");
            int defaultColor2 = this.TryReadAuraMonoStructIntField(faceRow, "defaultColor2");
            int facePartType = candidate.FacePartType;
            if (facePartType <= 0)
            {
                facePartType = this.TryReadAuraMonoStructIntField(faceRow, "type");
            }

            switch (facePartType)
            {
                case 0:
                    this.TrySetAuraFaceValueIntField(purchaseFace, "EyebrowStyle", candidate.StyleId);
                    this.TrySetAuraFaceValueIntField(purchaseFace, "EyebrowColor", defaultColor > 0 ? defaultColor : this.TryGetAuraFaceValueIntField(baselineFace, "EyebrowColor"));
                    break;
                case 1:
                    this.TrySetAuraFaceValueIntField(purchaseFace, "EyeSocketStyle", candidate.StyleId);
                    this.TrySetAuraFaceValueIntField(purchaseFace, "EyeSocketColor", defaultColor > 0 ? defaultColor : this.TryGetAuraFaceValueIntField(baselineFace, "EyeSocketColor"));
                    break;
                case 2:
                    this.TrySetAuraFaceValueIntField(purchaseFace, "EyeBallStyle", candidate.StyleId);
                    this.TrySetAuraFaceValueIntField(purchaseFace, "EyeBallColor", defaultColor > 0 ? defaultColor : this.TryGetAuraFaceValueIntField(baselineFace, "EyeBallColor"));
                    this.TrySetAuraFaceValueIntField(purchaseFace, "EyeBallColorR", defaultColor2 > 0 ? defaultColor2 : this.TryGetAuraFaceValueIntField(baselineFace, "EyeBallColorR"));
                    break;
                case 4:
                    this.TrySetAuraFaceValueIntField(purchaseFace, "PaintingStyle", candidate.StyleId);
                    this.TrySetAuraFaceValueIntField(purchaseFace, "PaintingColor", defaultColor > 0 ? defaultColor : this.TryGetAuraFaceValueIntField(baselineFace, "PaintingColor"));
                    break;
                default:
                    error = "unsupported face part type " + facePartType;
                    return false;
            }

            return true;
        }

        private bool TryInvokeAvatarFaceRefreshShopData(object faceSystem, Type faceSystemType, int shopId, out string error)
        {
            error = null;
            MethodInfo refresh = faceSystemType.GetMethod(
                "RefreshShopData",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(int) },
                null);
            if (refresh == null)
            {
                error = "RefreshShopData missing.";
                return false;
            }

            refresh.Invoke(faceSystem, new object[] { shopId });
            return true;
        }

        private bool TryInvokeAvatarFaceGetCurrent(object faceSystem, Type faceSystemType, out object faceValue, out string error)
        {
            faceValue = null;
            error = null;
            MethodInfo getCurrent = faceSystemType.GetMethod(
                "GetCurrentAvatar",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (getCurrent == null)
            {
                error = "GetCurrentAvatar missing.";
                return false;
            }

            faceValue = getCurrent.Invoke(faceSystem, null);
            if (faceValue == null)
            {
                error = "GetCurrentAvatar returned null.";
                return false;
            }

            return true;
        }

        private bool TryInvokeAvatarFaceSaveData(object faceSystem, Type faceSystemType, object faceValue, bool needIgnoreApply, out string error)
        {
            error = null;
            if (faceValue == null)
            {
                error = "face value null.";
                return false;
            }

            MethodInfo save = faceSystemType.GetMethod(
                "SaveFaceData",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { faceValue.GetType(), typeof(bool) },
                null);
            if (save == null)
            {
                error = "SaveFaceData missing.";
                return false;
            }

            save.Invoke(faceSystem, new object[] { faceValue, needIgnoreApply });
            return true;
        }

        private object TryCreateAvatarFaceValueCopy(object source, out Type faceValueType, out string error)
        {
            faceValueType = null;
            error = null;
            if (source == null)
            {
                error = "source face null.";
                return null;
            }

            faceValueType = source.GetType();
            object copy = Activator.CreateInstance(faceValueType);
            MethodInfo copyMethod = faceValueType.GetMethod(
                "Copy",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { faceValueType },
                null);
            if (copyMethod == null)
            {
                error = "AvatarFaceValue.Copy missing.";
                return null;
            }

            copyMethod.Invoke(copy, new[] { source });
            return copy;
        }

        private static void TrySetFaceValueIntField(object faceValue, string fieldName, int value)
        {
            if (faceValue == null)
            {
                return;
            }

            FieldInfo field = faceValue.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(faceValue, value);
            }
        }

        private static int TryGetFaceValueIntField(object faceValue, string fieldName)
        {
            if (faceValue == null)
            {
                return 0;
            }

            FieldInfo field = faceValue.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                return 0;
            }

            try
            {
                return Convert.ToInt32(field.GetValue(faceValue));
            }
            catch
            {
                return 0;
            }
        }

        private bool TryReadTableCoinPrice(Type tableDataType, string methodName, int id, out int price, out int currency)
        {
            price = 0;
            currency = 0;
            if (tableDataType == null || id <= 0)
            {
                return false;
            }

            MethodInfo getter = null;
            foreach (MethodInfo method in tableDataType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
                {
                    getter = method;
                    break;
                }
            }

            if (getter == null)
            {
                return false;
            }

            object row = getter.Invoke(null, new object[] { id });
            if (row == null)
            {
                return false;
            }

            this.TryGetManagedInt32Member(row, "price", out price);
            this.TryGetManagedInt32Member(row, "moneyValue", out currency);
            return true;
        }

        private static bool TryReadPartDataStatus(object partData, int unlockStatus, out int styleId)
        {
            styleId = 0;
            if (partData == null)
            {
                return false;
            }

            Type partDataType = partData.GetType();
            FieldInfo statusField = partDataType.GetField("status", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo styleField = partDataType.GetField("styleId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (statusField == null || styleField == null)
            {
                return false;
            }

            int status = Convert.ToInt32(statusField.GetValue(partData));
            styleId = Convert.ToInt32(styleField.GetValue(partData));
            return status == unlockStatus;
        }

        private static bool TryReadColorDataStatus(object colorData, int unlockStatus, out int colorId)
        {
            colorId = 0;
            if (colorData == null)
            {
                return false;
            }

            Type colorDataType = colorData.GetType();
            FieldInfo statusField = colorDataType.GetField("status", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo colorField = colorDataType.GetField("colorId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (statusField == null || colorField == null)
            {
                return false;
            }

            int status = Convert.ToInt32(statusField.GetValue(colorData));
            colorId = Convert.ToInt32(colorField.GetValue(colorData));
            return status == unlockStatus;
        }

        private void TryCollectFaceShopPartDictionary(
            IDictionary dictionary,
            string kind,
            string tableMethod,
            Type tableDataType,
            List<FaceShopBuyAllCandidate> items)
        {
            if (dictionary == null)
            {
                return;
            }

            foreach (DictionaryEntry entry in dictionary)
            {
                if (!(entry.Value is object partData) || !TryReadPartDataStatus(partData, FaceShopBuyAllUnlockStatus, out int styleId))
                {
                    continue;
                }

                if (!this.TryReadTableCoinPrice(tableDataType, tableMethod, styleId, out int price, out int currency)
                    || currency != FaceShopBuyAllCoinCurrency
                    || price <= 0)
                {
                    continue;
                }

                items.Add(new FaceShopBuyAllCandidate
                {
                    Kind = kind,
                    StyleId = styleId,
                    Price = price,
                    FacePartType = 0
                });
            }
        }

        private bool TryCollectFaceShopCoinItems(in FaceShopRuntime runtime, List<FaceShopBuyAllCandidate> items, out string error)
        {
            error = null;
            items.Clear();

            if (runtime.UseAura)
            {
                return this.TryCollectFaceShopCoinItemsAura(in runtime, items, out error);
            }

            object faceSystem = runtime.ManagedFaceSystem;
            Type faceSystemType = runtime.ManagedFaceSystemType;
            Type tableDataType = runtime.ManagedTableDataType;
            if (faceSystem == null || faceSystemType == null || tableDataType == null)
            {
                error = "AvatarFaceSystem unavailable.";
                return false;
            }

            if (!this.TryInvokeAvatarFaceRefreshShopData(faceSystem, faceSystemType, FaceShopBuyAllShopId, out error))
            {
                return false;
            }

            PropertyInfo faceDataResultProperty = faceSystemType.GetProperty(
                "FaceDataResult",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo colorDataResultProperty = faceSystemType.GetProperty(
                "ColorDataResult",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (faceDataResultProperty == null || colorDataResultProperty == null)
            {
                error = "Face shop data properties missing.";
                return false;
            }

            object faceDataResult = faceDataResultProperty.GetValue(faceSystem, null);
            object colorDataResult = colorDataResultProperty.GetValue(faceSystem, null);
            if (faceDataResult == null || colorDataResult == null)
            {
                error = "Face shop data empty.";
                return false;
            }

            Type faceDataType = faceDataResult.GetType();
            FieldInfo hairDataField = faceDataType.GetField("hairData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo beardDataField = faceDataType.GetField("beardData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo faceDataField = faceDataType.GetField("faceData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo skinDataField = faceDataType.GetField("skinData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (hairDataField == null || beardDataField == null || faceDataField == null)
            {
                error = "FaceData fields missing.";
                return false;
            }

            this.TryCollectFaceShopPartDictionary(hairDataField.GetValue(faceDataResult) as IDictionary, "hair", "GetHair", tableDataType, items);
            this.TryCollectFaceShopPartDictionary(beardDataField.GetValue(faceDataResult) as IDictionary, "beard", "GetBeard", tableDataType, items);

            if (faceDataField.GetValue(faceDataResult) is IDictionary facePartsByType)
            {
                foreach (DictionaryEntry facePartEntry in facePartsByType)
                {
                    int facePartType = 0;
                    try
                    {
                        facePartType = Convert.ToInt32(facePartEntry.Key);
                    }
                    catch
                    {
                        if (facePartEntry.Key != null && facePartEntry.Key.GetType().IsEnum)
                        {
                            facePartType = Convert.ToInt32(facePartEntry.Key);
                        }
                    }

                    if (!(facePartEntry.Value is IDictionary styleDictionary))
                    {
                        continue;
                    }

                    foreach (DictionaryEntry styleEntry in styleDictionary)
                    {
                        if (!(styleEntry.Value is object partData) || !TryReadPartDataStatus(partData, FaceShopBuyAllUnlockStatus, out int styleId))
                        {
                            continue;
                        }

                        if (!this.TryReadTableCoinPrice(tableDataType, "GetFace", styleId, out int price, out int currency)
                            || currency != FaceShopBuyAllCoinCurrency
                            || price <= 0)
                        {
                            continue;
                        }

                        items.Add(new FaceShopBuyAllCandidate
                        {
                            Kind = "face",
                            StyleId = styleId,
                            Price = price,
                            FacePartType = facePartType
                        });
                    }
                }
            }

            if (skinDataField?.GetValue(faceDataResult) is IEnumerable skinIds)
            {
                foreach (object skinIdObj in skinIds)
                {
                    int skinId = Convert.ToInt32(skinIdObj);
                    if (!(colorDataResult is IDictionary colorDictionary) || !colorDictionary.Contains(skinId))
                    {
                        continue;
                    }

                    if (!(colorDictionary[skinId] is object colorData) || !TryReadColorDataStatus(colorData, FaceShopBuyAllUnlockStatus, out int colorId))
                    {
                        continue;
                    }

                    if (!this.TryReadTableCoinPrice(tableDataType, "GetAvatarcolors", colorId, out int price, out int currency)
                        || currency != FaceShopBuyAllCoinCurrency
                        || price <= 0)
                    {
                        continue;
                    }

                    items.Add(new FaceShopBuyAllCandidate
                    {
                        Kind = "skin",
                        StyleId = colorId,
                        Price = price,
                        FacePartType = 1
                    });
                }
            }

            if (items.Count <= 0)
            {
                error = "No purchasable Coin face shop items.";
                return false;
            }

            return true;
        }

        private bool TryApplyFaceShopCandidate(
            object baselineFace,
            in FaceShopBuyAllCandidate candidate,
            Type tableDataType,
            out object purchaseFace,
            out string error)
        {
            purchaseFace = null;
            error = null;
            purchaseFace = this.TryCreateAvatarFaceValueCopy(baselineFace, out Type _, out error);
            if (purchaseFace == null)
            {
                return false;
            }

            if (string.Equals(candidate.Kind, "skin", StringComparison.Ordinal))
            {
                TrySetFaceValueIntField(purchaseFace, "SkinColor", candidate.StyleId);
                return true;
            }

            if (string.Equals(candidate.Kind, "hair", StringComparison.Ordinal))
            {
                MethodInfo getHair = tableDataType.GetMethod("GetHair", new[] { typeof(int), typeof(bool) })
                    ?? tableDataType.GetMethod("GetHair", new[] { typeof(int) });
                if (getHair == null)
                {
                    error = "GetHair missing.";
                    return false;
                }

                object[] args = getHair.GetParameters().Length == 2
                    ? new object[] { candidate.StyleId, true }
                    : new object[] { candidate.StyleId };
                if (!(getHair.Invoke(null, args) is object hairRow))
                {
                    error = "hair row missing.";
                    return false;
                }

                Type hairRowType = hairRow.GetType();
                MethodInfo validMain = hairRowType.GetMethod("GetValidMainColor", new[] { typeof(int) });
                MethodInfo validSub = hairRowType.GetMethod("GetValidSubColor", new[] { typeof(int) });
                int mainColor = TryGetFaceValueIntField(baselineFace, "HairColorMain");
                int subColor = TryGetFaceValueIntField(baselineFace, "HairColorSub");
                if (validMain != null)
                {
                    mainColor = Convert.ToInt32(validMain.Invoke(hairRow, new object[] { mainColor }));
                }

                if (validSub != null)
                {
                    subColor = Convert.ToInt32(validSub.Invoke(hairRow, new object[] { subColor }));
                }

                TrySetFaceValueIntField(purchaseFace, "HairStyle", candidate.StyleId);
                TrySetFaceValueIntField(purchaseFace, "HairColorMain", mainColor);
                TrySetFaceValueIntField(purchaseFace, "HairColorSub", subColor);
                return true;
            }

            if (string.Equals(candidate.Kind, "beard", StringComparison.Ordinal))
            {
                MethodInfo getBeard = tableDataType.GetMethod("GetBeard", new[] { typeof(int) });
                if (getBeard == null || !(getBeard.Invoke(null, new object[] { candidate.StyleId }) is object beardRow))
                {
                    error = "beard row missing.";
                    return false;
                }

                Type beardRowType = beardRow.GetType();
                MethodInfo validMain = beardRowType.GetMethod("GetValidMainColor", new[] { typeof(int) });
                MethodInfo validSub = beardRowType.GetMethod("GetValidSubColor", new[] { typeof(int) });
                int mainColor = TryGetFaceValueIntField(baselineFace, "BeardColorMain");
                int subColor = TryGetFaceValueIntField(baselineFace, "BeardColorSub");
                if (validMain != null)
                {
                    mainColor = Convert.ToInt32(validMain.Invoke(beardRow, new object[] { mainColor }));
                }

                if (validSub != null)
                {
                    subColor = Convert.ToInt32(validSub.Invoke(beardRow, new object[] { subColor }));
                }

                TrySetFaceValueIntField(purchaseFace, "BeardStyle", candidate.StyleId);
                TrySetFaceValueIntField(purchaseFace, "BeardColorMain", mainColor);
                TrySetFaceValueIntField(purchaseFace, "BeardColorSub", subColor);
                return true;
            }

            if (!string.Equals(candidate.Kind, "face", StringComparison.Ordinal))
            {
                error = "unknown candidate kind.";
                return false;
            }

            MethodInfo getFace = tableDataType.GetMethod("GetFace", new[] { typeof(int) });
            if (getFace == null || !(getFace.Invoke(null, new object[] { candidate.StyleId }) is object faceRow))
            {
                error = "face row missing.";
                return false;
            }

            this.TryGetManagedInt32Member(faceRow, "defaultColor", out int defaultColor);
            this.TryGetManagedInt32Member(faceRow, "defaultColor2", out int defaultColor2);
            int facePartType = candidate.FacePartType;
            if (facePartType <= 0)
            {
                this.TryGetManagedInt32Member(faceRow, "type", out facePartType);
            }

            switch (facePartType)
            {
                case 0:
                    TrySetFaceValueIntField(purchaseFace, "EyebrowStyle", candidate.StyleId);
                    TrySetFaceValueIntField(purchaseFace, "EyebrowColor", defaultColor > 0 ? defaultColor : TryGetFaceValueIntField(baselineFace, "EyebrowColor"));
                    break;
                case 1:
                    TrySetFaceValueIntField(purchaseFace, "EyeSocketStyle", candidate.StyleId);
                    TrySetFaceValueIntField(purchaseFace, "EyeSocketColor", defaultColor > 0 ? defaultColor : TryGetFaceValueIntField(baselineFace, "EyeSocketColor"));
                    break;
                case 2:
                    TrySetFaceValueIntField(purchaseFace, "EyeBallStyle", candidate.StyleId);
                    TrySetFaceValueIntField(purchaseFace, "EyeBallColor", defaultColor > 0 ? defaultColor : TryGetFaceValueIntField(baselineFace, "EyeBallColor"));
                    TrySetFaceValueIntField(purchaseFace, "EyeBallColorR", defaultColor2 > 0 ? defaultColor2 : TryGetFaceValueIntField(baselineFace, "EyeBallColorR"));
                    break;
                case 4:
                    TrySetFaceValueIntField(purchaseFace, "PaintingStyle", candidate.StyleId);
                    TrySetFaceValueIntField(purchaseFace, "PaintingColor", defaultColor > 0 ? defaultColor : TryGetFaceValueIntField(baselineFace, "PaintingColor"));
                    break;
                default:
                    error = "unsupported face part type " + facePartType;
                    return false;
            }

            return true;
        }

        private IEnumerator FaceShopBuyAllCoinRoutine()
        {
            yield return null;
            yield return null;

            string prepError;
            if (!this.TryResolveFaceShopRuntime(out FaceShopRuntime runtime, out prepError))
            {
                this.shopBuyAllStatus = prepError ?? "AvatarFaceSystem unavailable.";
                this.AddMenuNotification(this.shopBuyAllStatus, new Color(1f, 0.55f, 0.45f));
                this.shopBuyAllRunning = false;
                this.shopBuyAllCoroutine = null;
                yield break;
            }

            object baselineFaceManaged = null;
            IntPtr baselineFaceAura = IntPtr.Zero;
            if (runtime.UseAura)
            {
                if (!this.TryInvokeFaceShopAuraGetCurrent(in runtime, out baselineFaceAura, out prepError))
                {
                    this.shopBuyAllStatus = prepError ?? "Face shop baseline unavailable.";
                    this.AddMenuNotification(this.shopBuyAllStatus, new Color(1f, 0.55f, 0.45f));
                    this.shopBuyAllRunning = false;
                    this.shopBuyAllCoroutine = null;
                    yield break;
                }
            }
            else if (!this.TryInvokeAvatarFaceGetCurrent(runtime.ManagedFaceSystem, runtime.ManagedFaceSystemType, out baselineFaceManaged, out prepError))
            {
                this.shopBuyAllStatus = prepError ?? "Face shop baseline unavailable.";
                this.AddMenuNotification(this.shopBuyAllStatus, new Color(1f, 0.55f, 0.45f));
                this.shopBuyAllRunning = false;
                this.shopBuyAllCoroutine = null;
                yield break;
            }

            // The baseline face object lives across the whole multi-frame buy loop; root it so
            // the GC cannot collect it between yields (it may be referenced only by this routine).
            uint baselineFaceAuraPin = AuraMonoPinNew(baselineFaceAura);
            try
            {

            List<FaceShopBuyAllCandidate> items = new List<FaceShopBuyAllCandidate>();
            long coinBalance = long.MaxValue;
            this.TryGetPlayerCoinBalance(out coinBalance, out _);

            int bought = 0;
            int skipped = 0;
            int pass = 0;
            const int maxPasses = 64;
            while (pass < maxPasses)
            {
                pass++;
                if (!this.TryCollectFaceShopCoinItems(in runtime, items, out prepError) || items.Count == 0)
                {
                    if (bought == 0 && skipped == 0)
                    {
                        this.shopBuyAllStatus = prepError ?? "No purchasable Coin face shop items.";
                        this.AddMenuNotification(this.shopBuyAllStatus, new Color(1f, 0.55f, 0.45f));
                        this.shopBuyAllRunning = false;
                        this.shopBuyAllCoroutine = null;
                        yield break;
                    }

                    break;
                }

                bool boughtThisPass = false;
                for (int i = 0; i < items.Count; i++)
                {
                    FaceShopBuyAllCandidate candidate = items[i];
                    if (candidate.Price > 0 && coinBalance < candidate.Price)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        bool purchaseOk = false;
                        if (runtime.UseAura)
                        {
                            purchaseOk = this.TryApplyFaceShopCandidateAura(baselineFaceAura, in runtime, in candidate, out IntPtr purchaseFaceAura, out _)
                                && this.TryInvokeFaceShopAuraSaveData(in runtime, purchaseFaceAura, true, out _);
                        }
                        else
                        {
                            purchaseOk = this.TryApplyFaceShopCandidate(baselineFaceManaged, in candidate, runtime.ManagedTableDataType, out object purchaseFace, out _)
                                && this.TryInvokeAvatarFaceSaveData(runtime.ManagedFaceSystem, runtime.ManagedFaceSystemType, purchaseFace, true, out _);
                        }

                        if (purchaseOk)
                        {
                            bought++;
                            boughtThisPass = true;
                            if (candidate.Price > 0)
                            {
                                coinBalance -= candidate.Price;
                            }

                            this.shopBuyAllStatus = "Face shop bought " + bought + ": " + candidate.Kind + " " + candidate.StyleId;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        skipped++;
                        ModLogger.Msg("[FaceShopBuyAll] buy exception " + candidate.Kind + " " + candidate.StyleId + ": " + ex.Message);
                    }

                    yield return new WaitForSecondsRealtime(ShopBuyAllDelaySeconds);
                }

                if (!boughtThisPass)
                {
                    break;
                }
            }

            try
            {
                if (runtime.UseAura)
                {
                    this.TryInvokeFaceShopAuraSaveData(in runtime, baselineFaceAura, false, out _);
                }
                else
                {
                    this.TryInvokeAvatarFaceSaveData(runtime.ManagedFaceSystem, runtime.ManagedFaceSystemType, baselineFaceManaged, false, out _);
                }
            }
            catch
            {
            }

            this.shopBuyAllStatus = "Done (Face Shop): bought " + bought + ", skipped " + skipped + ".";
            this.AddMenuNotification(this.shopBuyAllStatus, new Color(0.55f, 1f, 0.65f));
            this.shopBuyAllRunning = false;
            this.shopBuyAllCoroutine = null;

            }
            finally
            {
                AuraMonoPinFree(baselineFaceAuraPin);
            }
        }
    }
}
