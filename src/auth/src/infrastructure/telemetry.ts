import { AzureMonitorTraceExporter } from "@azure/monitor-opentelemetry-exporter";
import { HttpInstrumentation } from "@opentelemetry/instrumentation-http";
import { PgInstrumentation } from "@opentelemetry/instrumentation-pg";
import { UndiciInstrumentation } from "@opentelemetry/instrumentation-undici";
import {
	core,
	NodeSDK,
	type NodeSDKConfiguration,
	tracing,
} from "@opentelemetry/sdk-node";

const healthRequestPattern = /^\/health\/?(?:\?|$)/i;

export function startTelemetry(): NodeSDK {
	const sdk = new NodeSDK({
		...exportConfiguration(),
		textMapPropagator: new core.W3CTraceContextPropagator(),
		sampler: new tracing.ParentBasedSampler({
			root: new tracing.AlwaysOnSampler(),
			remoteParentNotSampled: new tracing.AlwaysOnSampler(),
		}),
		metricReaders: [],
		logRecordProcessors: [],
		instrumentations: [
			new HttpInstrumentation({
				ignoreIncomingRequestHook: (request) =>
					healthRequestPattern.test(request.url ?? ""),
			}),
			new PgInstrumentation(),
			// instrumentation-undici records the query unredacted, a token in it would end up in the span
			new UndiciInstrumentation({
				startSpanHook: (request) => {
					const url = new URL(request.path, request.origin);
					url.search = "";
					return { "url.full": url.href, "url.query": undefined };
				},
			}),
		],
	});
	sdk.start();
	return sdk;
}

function exportConfiguration(): Partial<NodeSDKConfiguration> {
	const connectionString = process.env.APPLICATIONINSIGHTS_CONNECTION_STRING;
	if (connectionString) {
		return {
			traceExporter: new AzureMonitorTraceExporter({
				connectionString,
				disableOfflineStorage: true,
			}),
		};
	}
	// NodeSDK builds the OTLP exporter from the OTEL_EXPORTER_OTLP_* variables
	if (process.env.OTEL_EXPORTER_OTLP_ENDPOINT) {
		return {};
	}
	// Without an explicit empty list NodeSDK would fall back to an OTLP exporter on localhost
	return { spanProcessors: [] };
}
