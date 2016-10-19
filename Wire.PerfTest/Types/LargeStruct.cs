﻿using System;
using ProtoBuf;

namespace Wire.PerfTest.Types
{
    [Serializable]
    [ProtoContract]
    public struct LargeStruct
    {
        private static void A(bool b)
        {
            if (!b)
                throw new Exception();
        }

        [ProtoMember(1)] private ulong m_val1;
        [ProtoMember(2)] private ulong m_val2;
        [ProtoMember(3)] private ulong m_val3;
        [ProtoMember(4)] private ulong m_val4;

        private static ulong counter;

        public static LargeStruct Create()
        {
            return new LargeStruct
            {
                m_val1 = counter++,
                m_val2 = ulong.MaxValue - counter++,
                m_val3 = counter++,
                m_val4 = ulong.MaxValue - counter++
            };
        }

        public static void Compare(LargeStruct a, LargeStruct b)
        {
            A(a.m_val1 == b.m_val1);
            A(a.m_val2 == b.m_val2);
            A(a.m_val3 == b.m_val3);
            A(a.m_val4 == b.m_val4);
        }
    }
}