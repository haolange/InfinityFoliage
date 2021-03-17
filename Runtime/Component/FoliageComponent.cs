using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Collections.Generic;
using InfinityTech.Core.Geometry;
using System.Runtime.CompilerServices;

#if UNITY_EDITOR
using UnityEditor;
#endif

internal struct MTransfrom : IEquatable<MTransfrom>
{
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;

    public bool Equals(MTransfrom target)
    {
        return position != target.position || rotation != target.rotation || scale != target.scale;
    }

    public override bool Equals(object target)
    {
        return Equals((MTransfrom)target);
    }

    public override int GetHashCode()
    {
        return position.GetHashCode() + rotation.GetHashCode() + scale.GetHashCode();
    }
};

//[ExecuteInEditMode]
#if UNITY_EDITOR
[CanEditMultipleObjects]
#endif
public abstract unsafe class FoliageComponent : MonoBehaviour
{
    [HideInInspector]
    internal MTransfrom currTransform;
    [HideInInspector]
    internal MTransfrom prevTransform;
    
    public static List<FoliageComponent> s_foliageComponents = new List<FoliageComponent>(128);

    
    void OnEnable()
    {
        s_foliageComponents.Add(this);
        OnRegister();
        EventPlay();
    }

    void EventUpdate()
    {
        if (TransfromStateDirty())
        {
            OnTransformChange();
        }
        EventTick();
    }

    void OnDisable()
    {
        UnRegister();
        s_foliageComponents.Remove(this);
    }

    private bool TransfromStateDirty()
    {
        currTransform.position = transform.position;
        currTransform.rotation = transform.rotation;
        currTransform.scale = transform.localScale;

        if (currTransform.Equals(prevTransform))
        {
            prevTransform = currTransform;
            return true;
        }

        return false;
    }

    protected virtual void OnRegister()
    {

    }

    protected virtual void EventPlay()
    {

    }

    protected virtual void EventTick()
    {

    }

    protected virtual void OnTransformChange()
    {

    }

    protected virtual void UnRegister()
    {

    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract void InitViewFoliage(in float3 viewPos, in float4x4 matrixProj, FPlane* planes, in NativeList<JobHandle> taskHandles);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract void DispatchSetup(in NativeList<JobHandle> taskHandles);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract void DispatchDraw(CommandBuffer cmdBuffer);
}
