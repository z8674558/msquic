﻿//
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Performance.SDK;
using Microsoft.Performance.SDK.Extensibility;
using Microsoft.Performance.SDK.Processing;
using MsQuicTracing.DataModel;

namespace MsQuicTracing.Tables
{
    [Table]
    public sealed class QuicTxBlockedTable
    {
        public static readonly TableDescriptor TableDescriptor = new TableDescriptor(
           Guid.Parse("{64efbf30-7f58-4af1-9b8e-2cd81ac0c530}"),
           "QUIC TX Blocked State",
           "QUIC TX Blocked State",
           category: "Network",
           requiredDataCookers: new List<DataCookerPath> { QuicEventCooker.CookerPath });

        private static readonly ColumnConfiguration connectionColumnConfig =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{d85077b7-abbc-4f1b-b34e-c003f3cc2369}"), "Connection"),
                new UIHints { AggregationMode = AggregationMode.UniqueCount });

        private static readonly ColumnConfiguration processIdColumnConfig =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{7fd859c3-e483-415f-adbe-abb5d754906d}"), "Process (ID)"),
                new UIHints { AggregationMode = AggregationMode.Max });

        private static readonly ColumnConfiguration reasonColumnConfig =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{2dcf872d-e855-4a78-8258-7b466fe44f02}"), "Reason"),
                new UIHints { AggregationMode = AggregationMode.UniqueCount });

        private static readonly ColumnConfiguration timeColumnConfig =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{ede5f7bc-4587-499a-a51f-4b2e8d9db77e}"), "Time"),
                new UIHints { AggregationMode = AggregationMode.Max });

        private static readonly ColumnConfiguration durationColumnConfig =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{88230e20-4e79-4d37-aca4-f560a130841f}"), "Duration"),
                new UIHints { AggregationMode = AggregationMode.Sum });

        private static readonly ColumnConfiguration countColumnConfig =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{a647b420-1947-59e6-4468-d3b34c3dcbb0}"), "Count"),
                new UIHints { AggregationMode = AggregationMode.Sum });

        private static readonly ColumnConfiguration weightColumnConfig =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{196e2985-e16e-40f0-bbbb-3ce8e44d6555}"), "Weight"),
                new UIHints { AggregationMode = AggregationMode.Sum });

        private static readonly ColumnConfiguration percentWeightColumnConfig =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{56816a13-5b67-47c8-b7ff-c23abfdb4e75}"), "% Weight") { IsPercent = true },
                new UIHints { AggregationMode = AggregationMode.Sum });

        private static readonly TableConfiguration tableConfig1 =
            new TableConfiguration("Timeline by Process, Connection")
            {
                Columns = new[]
                {
                     processIdColumnConfig,
                     connectionColumnConfig,
                     reasonColumnConfig,
                     TableConfiguration.PivotColumn,
                     TableConfiguration.LeftFreezeColumn,
                     countColumnConfig,
                     weightColumnConfig,
                     percentWeightColumnConfig,
                     TableConfiguration.RightFreezeColumn,
                     TableConfiguration.GraphColumn,
                     timeColumnConfig,
                     durationColumnConfig,
                }
            };

        private static readonly TableConfiguration tableConfig2 =
            new TableConfiguration("Utilization by Process, Connection")
            {
                Columns = new[]
                {
                     processIdColumnConfig,
                     connectionColumnConfig,
                     reasonColumnConfig,
                     TableConfiguration.PivotColumn,
                     TableConfiguration.LeftFreezeColumn,
                     countColumnConfig,
                     weightColumnConfig,
                     timeColumnConfig,
                     durationColumnConfig,
                     TableConfiguration.RightFreezeColumn,
                     TableConfiguration.GraphColumn,
                     percentWeightColumnConfig,
                },
                ChartType = ChartType.StackedLine
            };

        public static void BuildTable(ITableBuilder tableBuilder, IDataExtensionRetrieval tableData)
        {
            Debug.Assert(!(tableBuilder is null) && !(tableData is null));

            var quicState = tableData.QueryOutput<QuicState>(new DataOutputPath(QuicEventCooker.CookerPath, "State"));
            if (quicState == null)
            {
                return;
            }

            var connections = quicState.Connections;
            if (connections.Count == 0)
            {
                return;
            }

            var data = connections.SelectMany(
                x => x.FlowBlockedEvents
                    .Where(x => x.Flags != QuicFlowBlockedFlags.None)
                    .Select(y => new ValueTuple<QuicConnection, QuicFlowBlockedData>(x, y))).ToArray();

            var table = tableBuilder.SetRowCount(data.Length);
            var dataProjection = Projection.Index(data);

            table.AddColumn(connectionColumnConfig, dataProjection.Compose(ProjectId));
            table.AddColumn(processIdColumnConfig, dataProjection.Compose(ProjectProcessId));
            table.AddColumn(reasonColumnConfig, dataProjection.Compose(ProjectReason));
            table.AddColumn(countColumnConfig, Projection.Constant<uint>(1));
            table.AddColumn(weightColumnConfig, dataProjection.Compose(ProjectWeight));
            table.AddColumn(percentWeightColumnConfig, dataProjection.Compose(ProjectPercentWeight));
            table.AddColumn(timeColumnConfig, dataProjection.Compose(ProjectTime));
            table.AddColumn(durationColumnConfig, dataProjection.Compose(ProjectDuration));

            tableConfig1.AddColumnRole(ColumnRole.StartTime, timeColumnConfig);
            tableConfig1.AddColumnRole(ColumnRole.Duration, durationColumnConfig);
            tableConfig1.InitialExpansionQuery = "[Series Name]:=\"Process (ID)\"";
            tableConfig1.InitialSelectionQuery = "[Series Name]:=\"Connection\" OR [Series Name]:=\"Reason\"";
            tableBuilder.AddTableConfiguration(tableConfig1);

            tableConfig2.AddColumnRole(ColumnRole.StartTime, timeColumnConfig);
            tableConfig2.AddColumnRole(ColumnRole.Duration, durationColumnConfig);
            tableConfig2.InitialExpansionQuery = "[Series Name]:=\"Process (ID)\"";
            tableConfig2.InitialSelectionQuery = "[Series Name]:=\"Reason\"";
            tableBuilder.AddTableConfiguration(tableConfig2);

            tableBuilder.SetDefaultTableConfiguration(tableConfig1);
        }

        #region Projections

        private static ulong ProjectId(ValueTuple<QuicConnection, QuicFlowBlockedData> data)
        {
            return data.Item1.Id;
        }

        private static uint ProjectProcessId(ValueTuple<QuicConnection, QuicFlowBlockedData> data)
        {
            return data.Item1.ProcessId;
        }

        private static string ProjectReason(ValueTuple<QuicConnection, QuicFlowBlockedData> data)
        {
            if (data.Item2.Flags.HasFlag(QuicFlowBlockedFlags.Scheduling))
            {
                return "Scheduling";
            }
            if (data.Item2.Flags.HasFlag(QuicFlowBlockedFlags.Pacing))
            {
                return "Pacing";
            }
            if (data.Item2.Flags.HasFlag(QuicFlowBlockedFlags.AmplificationProtection))
            {
                return "Amplification Protection";
            }
            if (data.Item2.Flags.HasFlag(QuicFlowBlockedFlags.CongestionControl))
            {
                return "Congestion Control";
            }
            if (data.Item2.Flags.HasFlag(QuicFlowBlockedFlags.ConnFlowControl))
            {
                return "Connection Flow Control";
            }
            if (data.Item2.Flags.HasFlag(QuicFlowBlockedFlags.StreamFlowControl))
            {
                return "Stream Flow Control";
            }
            if (data.Item2.Flags.HasFlag(QuicFlowBlockedFlags.App))
            {
                return "App";
            }
            if (data.Item2.Flags.HasFlag(QuicFlowBlockedFlags.StreamIdFlowControl))
            {
                return "Stream ID Flow Control";
            }
            return "None";
        }

        private static TimestampDelta ProjectWeight(ValueTuple<QuicConnection, QuicFlowBlockedData> data)
        {
            return data.Item2.Duration;
        }

        private static double ProjectPercentWeight(ValueTuple<QuicConnection, QuicFlowBlockedData> data)
        {
            TimestampDelta TimeNs = data.Item1.FinalTimeStamp - data.Item1.InitialTimeStamp;
            return 100.0 * data.Item2.Duration.ToNanoseconds / TimeNs.ToNanoseconds;
        }

        private static Timestamp ProjectTime(ValueTuple<QuicConnection, QuicFlowBlockedData> data)
        {
            return data.Item2.TimeStamp;
        }

        private static TimestampDelta ProjectDuration(ValueTuple<QuicConnection, QuicFlowBlockedData> data)
        {
            return data.Item2.Duration;
        }

        #endregion
    }
}