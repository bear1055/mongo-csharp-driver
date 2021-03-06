﻿/* Copyright 2018-present MongoDB Inc.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;

namespace MongoDB.Driver.Core.Operations
{
    internal static class RetryabilityHelper
    {
        // private constants
        private const string RetryableWriteErrorLabel = "RetryableWriteError";

        // private static fields
        private static readonly HashSet<ServerErrorCode> __notResumableChangeStreamErrorCodes;
        private static readonly HashSet<string> __notResumableChangeStreamErrorLabels;
        private static readonly HashSet<Type> __resumableChangeStreamExceptions;
        private static readonly HashSet<Type> __retryableReadExceptions;
        private static readonly HashSet<Type> __retryableWriteExceptions;
        private static readonly HashSet<ServerErrorCode> __retryableReadErrorCodes;
        private static readonly HashSet<ServerErrorCode> __retryableWriteErrorCodes;

        // static constructor
        static RetryabilityHelper()
        {
            var resumableAndRetryableExceptions = new HashSet<Type>()
            {
                typeof(MongoConnectionException),
                typeof(MongoNotPrimaryException),
                typeof(MongoNodeIsRecoveringException)
            };

            __resumableChangeStreamExceptions = new HashSet<Type>(resumableAndRetryableExceptions)
            {
                typeof(MongoCursorNotFoundException)
            };

            __retryableReadExceptions = new HashSet<Type>(resumableAndRetryableExceptions);

            __retryableWriteExceptions = new HashSet<Type>(resumableAndRetryableExceptions);

            var resumableAndRetryableErrorCodes = new HashSet<ServerErrorCode>
            {
                ServerErrorCode.HostNotFound,
                ServerErrorCode.HostUnreachable,
                ServerErrorCode.NetworkTimeout,
                ServerErrorCode.SocketException
            };

            __retryableReadErrorCodes = new HashSet<ServerErrorCode>(resumableAndRetryableErrorCodes);

            __retryableWriteErrorCodes = new HashSet<ServerErrorCode>(resumableAndRetryableErrorCodes)
            {
                ServerErrorCode.ExceededTimeLimit
            };

            __notResumableChangeStreamErrorCodes = new HashSet<ServerErrorCode>()
            {
                ServerErrorCode.CappedPositionLost,
                ServerErrorCode.CursorKilled,
                ServerErrorCode.Interrupted
            };

            __notResumableChangeStreamErrorLabels = new HashSet<string>()
            {
                "NonResumableChangeStreamError"
            };
        }

        // public static methods
        public static void AddRetryableWriteErrorLabelIfRequired(MongoException exception)
        {
            if (ShouldRetryableWriteExceptionLabelBeAdded(exception))
            {
                exception.AddErrorLabel(RetryableWriteErrorLabel);
            }
        }

        public static bool IsResumableChangeStreamException(Exception exception)
        {
            var commandException = exception as MongoCommandException;
            if (commandException != null)
            {
                var code = (ServerErrorCode)commandException.Code;
                var isNonResumable =
                    __notResumableChangeStreamErrorCodes.Contains(code) ||
                    __notResumableChangeStreamErrorLabels.Any(c => commandException.HasErrorLabel(c));
                return !isNonResumable;
            }
            else
            {
                return __resumableChangeStreamExceptions.Contains(exception.GetType());
            }
        }

        public static bool IsRetryableReadException(Exception exception)
        {
            if (__retryableReadExceptions.Contains(exception.GetType()))
            {
                return true;
            }

            var commandException = exception as MongoCommandException;
            if (commandException != null)
            {
                var code = (ServerErrorCode)commandException.Code;
                if (__retryableReadErrorCodes.Contains(code))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsRetryableWriteException(Exception exception)
        {
            return exception is MongoException mongoException ? mongoException.HasErrorLabel(RetryableWriteErrorLabel) : false;
        }

        // private static methods
        private static bool ShouldRetryableWriteExceptionLabelBeAdded(Exception exception)
        {
            if (__retryableWriteExceptions.Contains(exception.GetType()))
            {
                return true;
            }

            var commandException = exception as MongoCommandException;
            if (commandException != null)
            {
                var code = (ServerErrorCode)commandException.Code;
                if (__retryableWriteErrorCodes.Contains(code))
                {
                    return true;
                }
            }

            var writeConcernException = exception as MongoWriteConcernException;
            if (writeConcernException != null)
            {
                var writeConcernError = writeConcernException.WriteConcernResult.Response.GetValue("writeConcernError", null)?.AsBsonDocument;
                if (writeConcernError != null)
                {
                    var code = (ServerErrorCode)writeConcernError.GetValue("code", -1).AsInt32;
                    switch (code)
                    {
                        case ServerErrorCode.InterruptedAtShutdown:
                        case ServerErrorCode.InterruptedDueToReplStateChange:
                        case ServerErrorCode.NotMaster:
                        case ServerErrorCode.NotMasterNoSlaveOk:
                        case ServerErrorCode.NotMasterOrSecondary:
                        case ServerErrorCode.PrimarySteppedDown:
                        case ServerErrorCode.ShutdownInProgress:
                        case ServerErrorCode.HostNotFound:
                        case ServerErrorCode.HostUnreachable:
                        case ServerErrorCode.NetworkTimeout:
                        case ServerErrorCode.SocketException:
                        case ServerErrorCode.ExceededTimeLimit:
                            return true;
                    }
                }
            }

            return false;
        }
    }
}
