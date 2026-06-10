// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Diagnostics.Tracing;
using System.Text;

namespace DepotDownloader
{
    internal sealed class HttpDiagnosticEventListener : EventListener
    {
        public const EventKeywords TasksFlowActivityIds = (EventKeywords)0x80;

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "System.Net.Http" ||
                eventSource.Name == "System.Net.Sockets" ||
                eventSource.Name == "System.Net.Security" ||
                eventSource.Name == "System.Net.NameResolution")
            {
                EnableEvents(eventSource, EventLevel.LogAlways);
            }
            else if (eventSource.Name == "System.Threading.Tasks.TplEventSource")
            {
                EnableEvents(eventSource, EventLevel.LogAlways, TasksFlowActivityIds);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            var sb = new StringBuilder().Append($"{eventData.TimeStamp:HH:mm:ss.fffffff}  {eventData.EventSource.Name}.{eventData.EventName}(");
            var payloadCount = eventData.Payload?.Count ?? 0;
            for (var i = 0; i < payloadCount; i++)
            {
                var name = (eventData.PayloadNames != null && i < eventData.PayloadNames.Count) ? eventData.PayloadNames[i] : "?";
                sb.Append(name).Append(": ").Append(eventData.Payload[i]);
                if (i < payloadCount - 1)
                {
                    sb.Append(", ");
                }
            }

            sb.Append(')');
            Console.WriteLine(sb.ToString());
        }
    }
}
