using System;
using UnityEngine;

sealed class CoroutineInfo
{
    public Coroutine Coroutine;
    public Action WhenDone;
    public int LedIndex;
    public bool AbruptCancelAllowed;
}
