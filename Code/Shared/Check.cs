﻿#define SUPPRESSED

using System;

namespace MdfTools.Shared
{

    // this code should be unreachable
    public class UnexpectedExecutionPath : Exception
    {

    }

    public class Check
    {
        public static void ThrowUnexpectedExecutionPath() => throw new UnexpectedExecutionPath();

        public static void NotImplemented(Exception ex = null)
        {
#if RELEASE && !SUPPRESSED
#else
            ex ??= new NotImplementedException();
            throw ex;
#endif
        }

        public static void NotImplementedSuppressed(Exception ex = null)
        {
#if !SUPPRESSED
            ex ??= new NotImplementedException();
            throw ex;
#endif
        }

        public static void PleaseSendMeYourFile()
        {
            throw new NotImplementedException(
                "The author does not have access to a file using this feature. " +
                "Please create an issue and attach a sample file.");
        }
    }
}
