/* ===========================================================================
   Telemetrix dashboard — client application
   Vanilla JavaScript, no build step. Talks to the Telemetrix JSON API and
   renders traces, logs and metrics. uPlot is loaded lazily for charts.
   ========================================================================= */
(function () {
  "use strict";

  var CFG = window.TELEMETRIX || {};
  var BASE = (CFG.basePath || "/telemetrix").replace(/\/+$/, "");
  var UPLOT_CDN_JS = "https://cdn.jsdelivr.net/npm/uplot@1.6.32/dist/uPlot.iife.min.js";
  var UPLOT_CDN_CSS = "https://cdn.jsdelivr.net/npm/uplot@1.6.32/dist/uPlot.min.css";

  /* ----------------------------------------------------------------- state */
  var state = {
    tab: "overview",
    live: load("telemetrix.live", "1") === "1",
    theme: load("telemetrix.theme", "dark"),
    traceFilter: { q: "", status: "all" },
    logFilter: { q: "", level: "all" },
    timer: null,
    charts: {},
    booted: false,
    seq: 0,            // bumped on every tab switch; stale async renders are discarded
    refreshing: false, // guards against overlapping poll refreshes
    metricKeys: "",    // signature of the current metric set
  };

  /* ------------------------------------------------------------- utilities */
  function $(sel, root) { return (root || document).querySelector(sel); }
  function load(k, d) { try { return localStorage.getItem(k) || d; } catch (e) { return d; } }
  function save(k, v) { try { localStorage.setItem(k, v); } catch (e) { /* ignore */ } }

  function esc(s) {
    if (s == null) return "";
    return String(s).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }

  function fmtNum(n) {
    if (n == null) return "—";
    return Number(n).toLocaleString();
  }

  function fmtMs(ms) {
    if (ms == null || isNaN(ms)) return "—";
    if (ms < 1) return ms.toFixed(2) + " ms";
    if (ms < 1000) return (ms < 10 ? ms.toFixed(2) : ms < 100 ? ms.toFixed(1) : Math.round(ms)) + " ms";
    if (ms < 60000) return (ms / 1000).toFixed(2) + " s";
    return (ms / 60000).toFixed(1) + " min";
  }

  function ts(iso) { return Date.parse(iso); }

  function clock(iso) {
    var d = new Date(iso);
    return d.toLocaleTimeString([], { hour12: false }) +
      "." + String(d.getMilliseconds()).padStart(3, "0");
  }

  function ago(iso) {
    var s = (Date.now() - Date.parse(iso)) / 1000;
    if (s < 1) return "just now";
    if (s < 60) return Math.floor(s) + "s ago";
    if (s < 3600) return Math.floor(s / 60) + "m ago";
    if (s < 86400) return Math.floor(s / 3600) + "h ago";
    return Math.floor(s / 86400) + "d ago";
  }

  function api(path, opts) {
    return fetch(BASE + "/api" + path, opts || {}).then(function (r) {
      if (!r.ok) throw new Error("HTTP " + r.status);
      return r.status === 204 ? null : r.json();
    });
  }

  function toast(msg, isError) {
    var t = $("#toast");
    t.textContent = msg;
    t.className = "toast" + (isError ? " is-error" : "");
    t.hidden = false;
    clearTimeout(toast._t);
    toast._t = setTimeout(function () { t.hidden = true; }, 2600);
  }

  /* --------------------------------------------------------------- uPlot */
  var uplotReady = null;
  function loadAsset(tag, attr, url) {
    return new Promise(function (res, rej) {
      var e = document.createElement(tag);
      e[attr] = url;
      if (tag === "link") e.rel = "stylesheet";
      e.onload = function () { res(true); };
      e.onerror = function () { rej(new Error("load failed")); };
      document.head.appendChild(e);
    });
  }
  function ensureUplot() {
    if (uplotReady) return uplotReady;
    uplotReady = (function () {
      if (window.uPlot) return Promise.resolve(true);
      // Prefer a copy vendored into the assembly; fall back to a public CDN.
      return loadAsset("link", "href", BASE + "/assets/uplot.css").catch(function () {})
        .then(function () { return loadAsset("script", "src", BASE + "/assets/uplot.js"); })
        .then(function () { return !!window.uPlot; })
        .catch(function () {
          return loadAsset("link", "href", UPLOT_CDN_CSS).catch(function () {})
            .then(function () { return loadAsset("script", "src", UPLOT_CDN_JS); })
            .then(function () { return !!window.uPlot; })
            .catch(function () { return false; });
        });
    })();
    return uplotReady;
  }

  function themeColors() {
    var s = getComputedStyle(document.documentElement);
    return {
      muted: s.getPropertyValue("--text-muted").trim(),
      faint: s.getPropertyValue("--text-faint").trim(),
      grid: s.getPropertyValue("--border").trim(),
      accent: s.getPropertyValue("--accent").trim() || "#6366f1",
    };
  }

  function hexAlpha(hex, a) {
    if (!/^#[0-9a-f]{6}$/i.test(hex)) return hex;
    return hex + a;
  }

  function chart(host, key, points, color, mini) {
    if (!host) return;
    if (!window.uPlot) {
      host.innerHTML = '<div class="chart-empty">Charts need uPlot &mdash; offline and no cached copy</div>';
      return;
    }
    var w = host.clientWidth || 600;
    var hgt = host.clientHeight || (mini ? 80 : 168);
    if (w < 30) return;
    var xs = points.map(function (p) { return Math.floor(Date.parse(p.timestampUtc) / 1000); });
    var ys = points.map(function (p) { return p.value; });
    var data = [xs, ys];
    var c = themeColors();
    var existing = state.charts[key];
    if (existing && existing.u && existing.host === host && existing.w === w && existing.mini === mini) {
      try { existing.u.setData(data); return; } catch (e) { /* uPlot detached — rebuild below */ }
    }
    if (existing && existing.u) { try { existing.u.destroy(); } catch (e) {} }
    delete state.charts[key];
    host.innerHTML = "";

    var axisFont = "11px -apple-system, Segoe UI, Roboto, sans-serif";
    var opts = {
      width: w, height: hgt,
      cursor: { y: false, points: { size: 5 } },
      legend: { show: false },
      scales: { x: { time: true } },
      axes: mini
        ? [{ show: false }, { show: false }]
        : [
            { stroke: c.faint, font: axisFont, grid: { stroke: c.grid, width: 1 }, ticks: { stroke: c.grid, size: 4 }, size: 32 },
            { stroke: c.faint, font: axisFont, grid: { stroke: c.grid, width: 1 }, ticks: { stroke: c.grid, size: 4 }, size: 46 },
          ],
      series: [
        {},
        {
          stroke: color, width: 2,
          fill: hexAlpha(color, mini ? "20" : "26"),
          points: { show: points.length < 2 },
        },
      ],
    };
    try {
      state.charts[key] = { u: new uPlot(opts, data, host), w: w, mini: mini, host: host };
    } catch (e) {
      delete state.charts[key];
    }
  }

  function dropCharts() {
    Object.keys(state.charts).forEach(function (k) {
      try { state.charts[k].u.destroy(); } catch (e) {}
    });
    state.charts = {};
  }

  function dropMetricCharts() {
    Object.keys(state.charts).forEach(function (k) {
      if (k.indexOf("metric") === 0) {
        try { state.charts[k].u.destroy(); } catch (e) {}
        delete state.charts[k];
      }
    });
  }

  function resizeCharts() {
    Object.keys(state.charts).forEach(function (k) {
      var c = state.charts[k];
      if (!c || !c.u || !c.host) { return; }
      var w = c.host.clientWidth;
      if (w > 30) {
        try {
          c.u.setSize({ width: w, height: c.host.clientHeight || (c.mini ? 80 : 168) });
          c.w = w;
        } catch (e) { /* ignore */ }
      }
    });
  }

  /* ----------------------------------------------------------------- theme */
  var SUN = '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="4.2"/><path d="M12 2v2.5M12 19.5V22M2 12h2.5M19.5 12H22M4.9 4.9l1.8 1.8M17.3 17.3l1.8 1.8M19.1 4.9l-1.8 1.8M6.7 17.3l-1.8 1.8"/></svg>';
  var MOON = '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 14.5A8.3 8.3 0 0 1 9.5 4 8.3 8.3 0 1 0 20 14.5z"/></svg>';
  function applyTheme(t) {
    state.theme = t;
    document.documentElement.dataset.theme = t;
    save("telemetrix.theme", t);
    $("#themeBtn").innerHTML = t === "dark" ? SUN : MOON;
    dropCharts();
    if (state.booted) refresh();
  }

  /* ------------------------------------------------------------ live mode */
  function applyLive() {
    var btn = $("#liveBtn");
    btn.setAttribute("aria-pressed", state.live ? "true" : "false");
    $("#liveLabel").textContent = state.live ? "Live" : "Paused";
    save("telemetrix.live", state.live ? "1" : "0");
    if (state.timer) { clearInterval(state.timer); state.timer = null; }
    if (state.live) {
      state.timer = setInterval(function () { refresh(true); }, Math.max(750, CFG.refreshMs || 2000));
    }
    setConn(state.live ? "live" : "idle");
  }

  function setConn(mode) {
    var dot = $("#connDot");
    dot.className = "status-dot" +
      (mode === "live" ? " is-live" : mode === "error" ? " is-error" : mode === "stale" ? " is-stale" : "");
  }

  /* --------------------------------------------------------------- badges */
  function statusDot(status) {
    var s = (status || "unset").toLowerCase();
    return '<span class="dot ' + s + '" title="' + esc(status) + '"></span>';
  }
  function kindTag(kind) {
    var k = (kind || "internal").toLowerCase();
    return '<span class="kind-tag kind-' + k + '">' + esc(k) + "</span>";
  }
  function httpBadges(t) {
    var out = "";
    if (t.httpMethod) out += '<span class="badge method">' + esc(t.httpMethod) + "</span> ";
    if (t.httpStatusCode) {
      out += '<span class="badge http-' + Math.floor(t.httpStatusCode / 100) + '">' +
        t.httpStatusCode + "</span>";
    }
    return out || '<span class="faint small">&mdash;</span>';
  }

  /* ============================== OVERVIEW ============================== */
  var overview = {
    mount: function () {
      $("#view").innerHTML =
        '<div class="kpi-grid" id="kpis">' + skeletonKpis() + "</div>" +
        '<div class="chart-row">' +
          '<div class="card chart-card"><h3>Request throughput</h3>' +
            '<div class="chart-sub">traces per minute &middot; last 30 minutes</div>' +
            '<div class="chart-host" id="chThroughput"></div></div>' +
          '<div class="card chart-card"><h3>Latency p95</h3>' +
            '<div class="chart-sub">95th percentile duration &middot; last 30 minutes</div>' +
            '<div class="chart-host" id="chLatency"></div></div>' +
        "</div>" +
        '<div class="section-head"><h2>Recent errors</h2></div>' +
        '<div class="card" id="recentErrors"></div>';
    },
    refresh: function (seq) {
      return api("/stats").then(function (s) {
        if (seq !== state.seq) { return; }
        renderKpis(s);
        renderRecentErrors(s.recentErrors || []);
        ensureUplot().then(function () {
          if (seq !== state.seq) { return; }
          chart($("#chThroughput"), "throughput", s.throughput || [], themeColors().accent, false);
          chart($("#chLatency"), "latency", s.latency || [], "#f59e0b", false);
        });
      });
    },
  };

  function skeletonKpis() {
    var out = "";
    for (var i = 0; i < 6; i++) out += '<div class="card kpi"><div class="skeleton" style="height:58px"></div></div>';
    return out;
  }

  function kpi(label, value, unit, foot, cls) {
    return '<div class="card kpi ' + (cls || "") + '">' +
      '<div class="kpi-label">' + esc(label) + "</div>" +
      '<div class="kpi-value">' + value + (unit ? '<span class="unit">' + esc(unit) + "</span>" : "") + "</div>" +
      '<div class="kpi-foot">' + (foot || "") + "</div>" +
      '<div class="kpi-accent"></div></div>';
  }

  function renderKpis(s) {
    var el = $("#kpis");
    if (!el) { return; }
    var counts = s.logLevelCounts || {};
    var issues = (counts.Warning || 0) + (counts.Error || 0) + (counts.Critical || 0);
    el.innerHTML =
      kpi("Traces captured", fmtNum(s.traceCount), "", fmtNum(s.spanCount) + " spans") +
      kpi("Error rate", s.errorRatePercent.toFixed(1), "%", fmtNum(s.errorCount) + " failed traces",
        s.errorRatePercent > 0 ? "is-error" : "is-ok") +
      kpi("Latency p95", fmtMs(s.p95Ms).replace(/ (ms|s|min)$/, ""),
        fmtMs(s.p95Ms).replace(/^[\d.]+ /, ""), "p50 " + fmtMs(s.p50Ms) + " &middot; p99 " + fmtMs(s.p99Ms)) +
      kpi("Throughput", fmtNum(s.requestsPerMinute), "/min", "traces in the last minute") +
      kpi("SQL queries", fmtNum(s.sqlCount), "", fmtNum(s.metricSeriesCount) + " metric series") +
      kpi("Log issues", fmtNum(issues), "",
        fmtNum(s.logTotal) + " entries total", issues > 0 ? "is-warn" : "");
  }

  function renderRecentErrors(errors) {
    var el = $("#recentErrors");
    if (!el) { return; }
    if (!errors.length) {
      el.innerHTML = emptyInline("No errors captured", "Healthy so far.");
      return;
    }
    var rows = errors.map(function (t) {
      return '<tr data-trace="' + esc(t.traceId) + '">' +
        "<td>" + statusDot(t.status) + "</td>" +
        '<td><strong>' + esc(t.rootName) + "</strong></td>" +
        "<td>" + httpBadges(t) + "</td>" +
        '<td class="muted small">' + esc(t.serviceLabel || "—") + "</td>" +
        '<td class="muted small nowrap">' + ago(t.startTimeUtc) + "</td>" +
        '<td class="dur-text">' + fmtMs(t.durationMs) + "</td></tr>";
    }).join("");
    el.innerHTML =
      '<div class="table-wrap"><table class="grid"><tbody>' + rows + "</tbody></table></div>";
  }

  /* =============================== TRACES =============================== */
  var traces = {
    mount: function () {
      var f = state.traceFilter;
      $("#view").innerHTML =
        '<div class="toolbar">' +
          '<input class="input search" id="traceSearch" placeholder="Search by name, trace id or service…" value="' + esc(f.q) + '">' +
          '<div class="seg" id="traceStatus">' +
            seg("all", "All", f.status) + seg("ok", "OK", f.status) + seg("error", "Errors", f.status) +
          "</div>" +
          '<span class="spacer"></span><span class="muted small" id="traceCount"></span>' +
        "</div>" +
        '<div class="card table-wrap" id="traceResults"></div>';
      var searchEl = $("#traceSearch");
      searchEl.addEventListener("input", debounce(function () {
        state.traceFilter.q = searchEl.value.trim();
        refresh();
      }, 250));
      $("#traceStatus").addEventListener("click", function (e) {
        var b = e.target.closest("button[data-v]");
        if (!b) return;
        state.traceFilter.status = b.dataset.v;
        traces.mount();
        refresh();
      });
    },
    refresh: function (seq) {
      var f = state.traceFilter;
      var q = "?limit=200&status=" + encodeURIComponent(f.status) +
        (f.q ? "&q=" + encodeURIComponent(f.q) : "");
      return api("/traces" + q).then(function (d) {
        if (seq !== state.seq) { return; }
        renderTraceTable(d.traces || []);
      });
    },
  };

  function renderTraceTable(list) {
    var results = $("#traceResults");
    if (!results) { return; }
    var countEl = $("#traceCount");
    if (countEl) { countEl.textContent = list.length + " trace" + (list.length === 1 ? "" : "s"); }
    if (!list.length) {
      results.innerHTML = emptyState("No traces yet",
        "Send a request to your application and it will appear here.");
      return;
    }
    var maxDur = Math.max.apply(null, list.map(function (t) { return t.durationMs || 0; })) || 1;
    var rows = list.map(function (t) {
      var w = Math.max(3, Math.round((t.durationMs / maxDur) * 70));
      return '<tr data-trace="' + esc(t.traceId) + '">' +
        "<td>" + statusDot(t.status) + "</td>" +
        '<td><strong>' + esc(t.rootName) + "</strong>" + kindBit(t.rootKind) + "</td>" +
        "<td>" + httpBadges(t) + "</td>" +
        '<td class="muted small">' + esc(t.serviceLabel || "—") + "</td>" +
        '<td class="muted small">' + t.spanCount + "</td>" +
        "<td>" + sqlCell(t.sqlCount) + "</td>" +
        '<td class="muted small nowrap">' + ago(t.startTimeUtc) + "</td>" +
        '<td><div class="dur-cell"><span class="dur-bar" style="width:' + w + 'px;' +
          (t.status && t.status.toLowerCase() === "error" ? "background:var(--error)" : "") + '"></span>' +
          '<span class="dur-text">' + fmtMs(t.durationMs) + "</span></div></td></tr>";
    }).join("");
    results.innerHTML =
      "<table class=\"grid\"><thead><tr>" +
      "<th></th><th>Root operation</th><th>Endpoint</th><th>Service</th>" +
      "<th>Spans</th><th>SQL</th><th>Started</th><th style=\"text-align:right\">Duration</th>" +
      "</tr></thead><tbody>" + rows + "</tbody></table>";
  }

  function kindBit(kind) {
    var k = (kind || "internal").toLowerCase();
    if (k === "server" || k === "internal") return "";
    return ' <span class="kind-tag kind-' + k + '" style="font-size:9px">' + esc(k) + "</span>";
  }
  function sqlCell(n) {
    if (!n) return '<span class="faint small">—</span>';
    return '<span class="badge" style="background:color-mix(in srgb,var(--k-sql) 18%,transparent);color:var(--k-sql)">' +
      n + "</span>";
  }

  /* ================================ LOGS ================================ */
  var logs = {
    mount: function () {
      var f = state.logFilter;
      var levels = [["all", "All levels"], ["0", "Trace +"], ["1", "Debug +"],
        ["2", "Information +"], ["3", "Warning +"], ["4", "Error +"]];
      $("#view").innerHTML =
        '<div class="toolbar">' +
          '<input class="input search" id="logSearch" placeholder="Search log messages and categories…" value="' + esc(f.q) + '">' +
          '<select class="select" id="logLevel">' +
            levels.map(function (l) {
              return '<option value="' + l[0] + '"' + (f.level === l[0] ? " selected" : "") + ">" + l[1] + "</option>";
            }).join("") +
          "</select>" +
          '<span class="spacer"></span><span class="muted small" id="logCount"></span>' +
        "</div>" +
        '<div class="card table-wrap" id="logResults"></div>';
      var searchEl = $("#logSearch");
      searchEl.addEventListener("input", debounce(function () {
        state.logFilter.q = searchEl.value.trim();
        refresh();
      }, 250));
      $("#logLevel").addEventListener("change", function (e) {
        state.logFilter.level = e.target.value;
        refresh();
      });
    },
    refresh: function (seq) {
      var f = state.logFilter;
      var q = "?limit=400&level=" + encodeURIComponent(f.level) +
        (f.q ? "&q=" + encodeURIComponent(f.q) : "");
      return api("/logs" + q).then(function (d) {
        if (seq !== state.seq) { return; }
        renderLogTable(d.logs || []);
      });
    },
  };

  function renderLogTable(list) {
    var results = $("#logResults");
    if (!results) { return; }
    var countEl = $("#logCount");
    if (countEl) { countEl.textContent = list.length + " entr" + (list.length === 1 ? "y" : "ies"); }
    if (!list.length) {
      results.innerHTML = emptyState("No log entries", "Logs written by your application will stream in here.");
      return;
    }
    var rows = list.map(function (l) {
      return '<tr class="log-row">' +
        '<td class="muted small nowrap mono">' + clock(l.timestampUtc) + "</td>" +
        '<td><span class="log-level lvl-' + esc(l.level) + '">' + esc(l.level) + "</span></td>" +
        '<td class="muted small">' + esc(shortCategory(l.category)) + "</td>" +
        '<td class="log-msg">' + esc(l.message) +
          (l.exception ? ' <span class="badge http-5">exception</span>' : "") + "</td></tr>" +
        '<tr class="log-detail" hidden><td colspan="4">' + logDetailHtml(l) + "</td></tr>";
    }).join("");
    results.innerHTML =
      "<table class=\"grid\"><thead><tr><th>Time</th><th>Level</th><th>Category</th><th>Message</th>" +
      "</tr></thead><tbody>" + rows + "</tbody></table>";
  }

  function shortCategory(c) {
    if (!c) return "—";
    var parts = c.split(".");
    return parts.length > 2 ? "…" + parts.slice(-2).join(".") : c;
  }

  function logDetailHtml(l) {
    var html = '<div class="log-detail-inner">';
    html += '<div class="kv">';
    html += "<dt>category</dt><dd>" + esc(l.category || "—") + "</dd>";
    html += "<dt>timestamp</dt><dd>" + esc(new Date(l.timestampUtc).toISOString()) + "</dd>";
    if (l.eventId) html += "<dt>event id</dt><dd>" + l.eventId + (l.eventName ? " (" + esc(l.eventName) + ")" : "") + "</dd>";
    if (l.traceId) {
      html += "<dt>trace</dt><dd><a href=\"#\" data-open-trace=\"" + esc(l.traceId) +
        "\" class=\"mono\">" + esc(l.traceId.slice(0, 16)) + "…</a></dd>";
    }
    html += "</div>";
    if (l.attributes && l.attributes.length) {
      html += '<div style="margin-top:10px"><div class="drawer-section" style="padding:0"><h3>Attributes</h3></div>' +
        tagTable(l.attributes) + "</div>";
    }
    if (l.exception) {
      html += '<div class="exception-box"><div class="ex-type">' + esc(l.exception.type) + "</div>" +
        '<div class="muted" style="margin-top:3px">' + esc(l.exception.message) + "</div>" +
        (l.exception.stackTrace ? "<pre>" + esc(l.exception.stackTrace) + "</pre>" : "") + "</div>";
    }
    html += "</div>";
    return html;
  }

  /* ============================== METRICS =============================== */
  var metrics = {
    mount: function () {
      $("#view").innerHTML = '<div class="metric-grid" id="metricGrid"></div>';
    },
    refresh: function (seq) {
      return api("/metrics").then(function (d) {
        if (seq !== state.seq) { return; }
        var grid = $("#metricGrid");
        if (!grid) { return; }
        var list = d.metrics || [];
        if (!list.length) {
          grid.innerHTML = emptyState("No metrics yet",
            "Counters, gauges and histograms collected from your meters will appear here.");
          state.metricKeys = "";
          return;
        }
        var sig = list.map(function (m) {
          return m.meterName + "|" + m.name + "|" +
            (m.tags || []).map(function (t) { return t.key + "=" + t.value; }).join(",");
        }).join("~~");
        if (sig !== state.metricKeys) {
          // The set of series changed — rebuild the grid and the charts.
          dropMetricCharts();
          grid.innerHTML = list.map(metricCard).join("");
          state.metricKeys = sig;
        } else {
          // Same series — update the headline values in place; charts refresh below.
          list.forEach(function (m, i) {
            var valueEl = document.getElementById("mv" + i);
            if (!valueEl) { return; }
            var lp = m.points && m.points.length ? m.points[m.points.length - 1] : null;
            valueEl.innerHTML = (lp ? formatMetric(lp.value) : "&mdash;") +
              (m.unit ? ' <span class="unit">' + esc(m.unit) + "</span>" : "");
          });
        }
        ensureUplot().then(function () {
          if (seq !== state.seq) { return; }
          list.forEach(function (m, i) {
            chart(document.getElementById("mc" + i), "metric" + i, m.points || [], themeColors().accent, true);
          });
        });
      });
    },
  };

  function metricCard(m, i) {
    var last = m.points && m.points.length ? m.points[m.points.length - 1] : null;
    var value = last ? formatMetric(last.value) : "—";
    var tags = (m.tags || []).map(function (t) {
      return '<span class="metric-tag">' + esc(t.key) + "=" + esc(t.value) + "</span>";
    }).join("");
    return '<div class="card metric-card">' +
      '<div class="metric-card-head">' +
        '<div><div class="metric-name">' + esc(m.name) + "</div>" +
        '<div class="metric-meter">' + esc(m.meterName) + "</div></div>" +
        '<span class="metric-type">' + esc(m.instrumentType) + "</span>" +
      "</div>" +
      '<div class="metric-value" id="mv' + i + '">' + value +
        (m.unit ? ' <span class="unit">' + esc(m.unit) + "</span>" : "") + "</div>" +
      (m.description ? '<div class="metric-desc">' + esc(m.description) + "</div>" : "") +
      '<div class="metric-chart" id="mc' + i + '"></div>' +
      (tags ? '<div class="metric-tags">' + tags + "</div>" : "") +
      "</div>";
  }

  function formatMetric(v) {
    if (v == null) return "—";
    if (Math.abs(v) >= 1000 || v === Math.floor(v)) return fmtNum(Math.round(v * 100) / 100);
    return v.toFixed(2);
  }

  /* =============================== DRAWER =============================== */
  function openTrace(id) {
    var drawer = $("#drawer");
    drawer.hidden = false;
    $("#drawerBody").innerHTML = '<div class="drawer-section"><div class="skeleton" style="height:280px"></div></div>';
    api("/traces/" + encodeURIComponent(id)).then(function (t) {
      renderTrace(t);
    }).catch(function () {
      $("#drawerBody").innerHTML = '<div class="drawer-section">' +
        emptyState("Trace unavailable", "It may have been evicted from the buffer.") + "</div>";
    });
  }
  function closeDrawer() { $("#drawer").hidden = true; }

  function renderTrace(t) {
    var spans = t.spans || [];
    var head =
      '<div class="drawer-head"><div class="drawer-head-top">' +
        '<div><h2 class="drawer-title">' + esc(t.rootName) + "</h2>" +
        '<div class="mono small faint" style="margin-top:3px">' + esc(t.traceId) + "</div></div>" +
        '<button class="drawer-close" data-close="1" aria-label="Close">&times;</button>' +
      "</div>" +
      '<div class="drawer-meta">' +
        meta("Status", '<span class="' + (t.status.toLowerCase() === "error" ? "" : "") + '">' +
          statusDot(t.status) + " " + esc(t.status) + "</span>") +
        meta("Duration", fmtMs(t.durationMs)) +
        meta("Spans", fmtNum(t.spanCount)) +
        meta("SQL", fmtNum(t.sqlCount)) +
        meta("Errors", fmtNum(t.errorCount)) +
        meta("Started", new Date(t.startTimeUtc).toLocaleTimeString([], { hour12: false })) +
      "</div></div>";

    var body = '<div class="drawer-section"><h3>Span waterfall</h3>' + waterfallHtml(spans, t) + "</div>";

    if (t.logs && t.logs.length) {
      body += '<div class="drawer-section"><h3>Correlated logs (' + t.logs.length + ")</h3>" +
        '<div class="card table-wrap"><table class="grid"><tbody>' +
        t.logs.map(function (l) {
          return '<tr class="log-row">' +
            '<td class="muted small nowrap mono">' + clock(l.timestampUtc) + "</td>" +
            '<td><span class="log-level lvl-' + esc(l.level) + '">' + esc(l.level) + "</span></td>" +
            '<td class="log-msg">' + esc(l.message) + "</td></tr>" +
            '<tr class="log-detail" hidden><td colspan="3">' + logDetailHtml(l) + "</td></tr>";
        }).join("") +
        "</tbody></table></div></div>";
    }

    $("#drawerBody").innerHTML = head + body;
  }

  function meta(k, v) {
    return '<div class="meta-item"><span class="meta-k">' + esc(k) + '</span><span class="meta-v">' + v + "</span></div>";
  }

  function waterfallHtml(spans, trace) {
    if (!spans.length) return emptyInline("No spans", "");
    var order = buildTree(spans);
    var t0 = Math.min.apply(null, spans.map(function (s) { return ts(s.startTimeUtc); }));
    var t1 = Math.max.apply(null, spans.map(function (s) { return ts(s.startTimeUtc) + s.durationMs; }));
    var total = Math.max(t1 - t0, 0.001);

    var ruler = '<div class="wf-ruler">';
    for (var i = 0; i <= 4; i++) {
      ruler += '<div class="wf-tick" style="left:' + (i * 25) + '%"><span>' +
        fmtMs(total * i / 4) + "</span></div>";
    }
    ruler += "</div>";

    var rows = order.map(function (node) {
      var s = node.span;
      var left = ((ts(s.startTimeUtc) - t0) / total) * 100;
      var width = Math.max((s.durationMs / total) * 100, 0.6);
      if (left + width > 100) left = Math.max(0, 100 - width);
      var isErr = (s.status || "").toLowerCase() === "error";
      var barKind = s.source === "sql" ? "k-sql" : "k-" + (s.kind || "internal").toLowerCase();
      var pad = node.depth * 14;
      return '<div class="wf-row" data-span="' + esc(s.spanId) + '">' +
          '<div class="wf-label" style="padding-left:' + pad + 'px">' +
            '<span class="wf-caret">▸</span>' +
            '<span class="wf-kind-bar" style="background:var(--' +
              (s.source === "sql" ? "k-sql" : "k-" + (s.kind || "internal").toLowerCase()) + ')"></span>' +
            '<span class="wf-name" title="' + esc(s.name) + '">' + esc(s.name) + "</span>" +
          "</div>" +
          '<div class="wf-track">' +
            '<div class="wf-bar ' + barKind + (isErr ? " is-error" : "") +
              '" style="left:' + left + "%;width:" + width + '%">' +
              '<span class="wf-bar-dur">' + fmtMs(s.durationMs) + "</span>" +
            "</div>" +
          "</div>" +
        "</div>" +
        '<div class="wf-detail" hidden>' + spanDetailHtml(s, trace) + "</div>";
    }).join("");

    return '<div class="waterfall">' + ruler + rows + "</div>";
  }

  function buildTree(spans) {
    var byId = {}, kids = {}, roots = [];
    spans.forEach(function (s) { byId[s.spanId] = s; });
    spans.forEach(function (s) {
      if (s.parentSpanId && byId[s.parentSpanId] && s.parentSpanId !== s.spanId) {
        (kids[s.parentSpanId] = kids[s.parentSpanId] || []).push(s);
      } else {
        roots.push(s);
      }
    });
    var byStart = function (a, b) { return ts(a.startTimeUtc) - ts(b.startTimeUtc); };
    var order = [], seen = {};
    function walk(s, depth) {
      if (seen[s.spanId]) return;
      seen[s.spanId] = 1;
      order.push({ span: s, depth: depth });
      (kids[s.spanId] || []).slice().sort(byStart).forEach(function (k) { walk(k, depth + 1); });
    }
    roots.sort(byStart).forEach(function (r) { walk(r, 0); });
    spans.forEach(function (s) { if (!seen[s.spanId]) order.push({ span: s, depth: 0 }); });
    return order;
  }

  function spanDetailHtml(s, trace) {
    var html = '<div class="detail-grid"><div>';
    html += '<div class="kv">';
    html += "<dt>span id</dt><dd>" + esc(s.spanId) + "</dd>";
    html += "<dt>kind</dt><dd>" + kindTag(s.kind) + "</dd>";
    html += "<dt>duration</dt><dd>" + fmtMs(s.durationMs) + "</dd>";
    html += "<dt>started</dt><dd>" + esc(clock(s.startTimeUtc)) + "</dd>";
    if (s.sourceName) html += "<dt>source</dt><dd>" + esc(s.sourceName) + "</dd>";
    if (s.statusDescription) html += "<dt>status</dt><dd>" + esc(s.statusDescription) + "</dd>";
    html += "</div></div><div>";
    if (s.tags && s.tags.length) {
      html += '<div class="meta-k" style="margin-bottom:6px">Attributes</div>' + tagTable(s.tags);
    }
    html += "</div></div>";

    if (s.sql) html += '<div style="margin-top:14px">' + sqlInspectorHtml(s.sql) + "</div>";

    if (s.events && s.events.length) {
      html += '<div style="margin-top:12px"><div class="meta-k" style="margin-bottom:6px">Events</div>';
      html += s.events.map(function (e) {
        return '<div class="badge" style="margin:2px 4px 2px 0">' + esc(e.name) + " · " + esc(clock(e.timestampUtc)) + "</div>";
      }).join("");
      html += "</div>";
    }

    var spanLogs = (trace.logs || []).filter(function (l) { return l.spanId === s.spanId; });
    if (spanLogs.length) {
      html += '<div style="margin-top:12px"><div class="meta-k" style="margin-bottom:6px">Logs on this span</div>';
      html += spanLogs.map(function (l) {
        return '<div class="log-msg" style="padding:3px 0">' +
          '<span class="log-level lvl-' + esc(l.level) + '">' + esc(l.level) + "</span> " +
          esc(l.message) + "</div>";
      }).join("");
      html += "</div>";
    }
    return html;
  }

  function sqlInspectorHtml(sql) {
    var html = '<div class="sql-inspector">';
    html += '<div class="sql-inspector-head">' +
      '<span class="kind-tag kind-sql">SQL</span>' +
      '<span class="muted">' + esc(sql.provider || "") + "</span>" +
      (sql.database ? '<span class="muted">&middot; ' + esc(sql.database) + "</span>" : "") +
      '<span class="spacer"></span>' +
      '<span class="muted small">' + fmtMs(sql.durationMs) +
        (sql.rowsAffected >= 0 ? " &middot; " + sql.rowsAffected + " rows" : "") + "</span>" +
      "</div>";
    html += '<pre class="sql-code">' + highlightSql(sql.formattedSql || sql.commandText || "") + "</pre>";

    if (sql.codeLocation) {
      var cl = sql.codeLocation;
      var where = cl.fileName ? esc(cl.fileName) + (cl.line ? ":" + cl.line : "") : "(no debug symbols)";
      html += '<div class="code-loc"><span class="loc-icon">&#9656;</span>' +
        "<span>" + where + "</span>" +
        (cl.member ? '<span class="muted">&middot; ' + esc(cl.member) + "</span>" : "") + "</div>";
    }

    if (sql.parameters && sql.parameters.length) {
      html += '<table class="param-table"><thead><tr>' +
        "<th>Parameter</th><th>Value</th><th>Type</th><th>Direction</th></tr></thead><tbody>";
      html += sql.parameters.map(function (p) {
        return "<tr>" +
          '<td class="pname">' + esc(p.name) + "</td>" +
          '<td class="pval' + (p.isNull ? " is-null" : "") + '">' + (p.isNull ? "NULL" : esc(p.value)) + "</td>" +
          '<td class="muted small">' + esc(p.dbType || "—") + "</td>" +
          '<td class="muted small">' + esc(p.direction || "Input") + "</td></tr>";
      }).join("");
      html += "</tbody></table>";
    }

    if (sql.isError && sql.errorMessage) {
      html += '<div class="exception-box" style="margin:10px 12px"><div class="muted">' +
        esc(sql.errorMessage) + "</div></div>";
    }
    html += "</div>";
    return html;
  }

  var SQL_KW = /\b(SELECT|INSERT|INTO|UPDATE|DELETE|FROM|WHERE|AND|OR|NOT|NULL|JOIN|LEFT|RIGHT|INNER|OUTER|FULL|CROSS|ON|AS|GROUP|ORDER|BY|HAVING|LIMIT|OFFSET|VALUES|SET|DISTINCT|COUNT|SUM|AVG|MIN|MAX|TOP|UNION|ALL|EXISTS|IN|IS|LIKE|BETWEEN|CASE|WHEN|THEN|ELSE|END|ASC|DESC|RETURNING|INTO)\b/gi;
  function highlightSql(sql) {
    var h = esc(sql);
    h = h.replace(SQL_KW, function (m) { return '<span class="sql-kw">' + m + "</span>"; });
    h = h.replace(/\b(\d+\.?\d*)\b/g, function (m) { return '<span class="sql-num">' + m + "</span>"; });
    return h;
  }

  /* ---------------------------------------------------------- shared bits */
  function tagTable(tags) {
    return '<div class="kv">' + tags.map(function (t) {
      return "<dt>" + esc(t.key) + "</dt><dd>" + esc(t.value == null ? "—" : t.value) + "</dd>";
    }).join("") + "</div>";
  }
  function seg(v, label, active) {
    return '<button data-v="' + v + '"' + (active === v ? ' class="is-active"' : "") + ">" + label + "</button>";
  }
  function emptyState(title, sub) {
    return '<div class="empty"><div class="empty-mark">&#9671;</div><h3>' + esc(title) +
      "</h3><div>" + esc(sub) + "</div></div>";
  }
  function emptyInline(title, sub) {
    return '<div class="empty" style="padding:34px 20px"><h3>' + esc(title) + "</h3>" +
      (sub ? "<div>" + esc(sub) + "</div>" : "") + "</div>";
  }
  function debounce(fn, ms) {
    var t;
    return function () { clearTimeout(t); t = setTimeout(fn, ms); };
  }

  /* --------------------------------------------------------------- tabs */
  var TABS = { overview: overview, traces: traces, logs: logs, metrics: metrics };

  function selectTab(name) {
    if (!TABS[name]) name = "overview";
    state.tab = name;
    state.seq++;          // invalidate any in-flight refresh from the previous tab
    state.metricKeys = "";
    Array.prototype.forEach.call(document.querySelectorAll(".tab"), function (b) {
      b.classList.toggle("is-active", b.dataset.tab === name);
    });
    dropCharts();
    TABS[name].mount();
    refresh();
  }

  function refresh(isPoll) {
    var view = TABS[state.tab];
    if (!view) { return Promise.resolve(); }
    // Never let polls stack up behind a slow request.
    if (isPoll && state.refreshing) { return Promise.resolve(); }
    state.refreshing = true;
    var seq = state.seq;
    return Promise.resolve()
      .then(function () { return view.refresh(seq); })
      .then(function () {
        if (state.seq !== seq) { return; }
        setConn(state.live ? "live" : "idle");
        var updated = $("#lastUpdated");
        if (updated) {
          updated.textContent = "updated " + new Date().toLocaleTimeString([], { hour12: false });
        }
      })
      .catch(function () {
        if (state.seq !== seq) { return; }
        setConn("error");
        if (!isPoll) { toast("Could not reach the Telemetrix API", true); }
      })
      .then(function () { state.refreshing = false; });
  }

  /* --------------------------------------------------------------- boot */
  function boot() {
    document.documentElement.style.setProperty("--accent", CFG.accent || "#6366f1");
    $("#envPill").textContent = CFG.environment || "Development";
    $("#footVersion").textContent = "Telemetrix v" + (CFG.version || "0.1.0");
    applyTheme(state.theme);
    applyLive();

    $("#themeBtn").addEventListener("click", function () {
      applyTheme(state.theme === "dark" ? "light" : "dark");
    });
    $("#liveBtn").addEventListener("click", function () {
      state.live = !state.live;
      applyLive();
      if (state.live) refresh();
    });
    $("#refreshBtn").addEventListener("click", function () { refresh(); });
    $("#clearBtn").addEventListener("click", function () {
      if (!confirm("Discard all captured traces, logs and metrics?")) return;
      api("/clear", { method: "POST" }).then(function () {
        toast("Telemetry cleared");
        refresh();
      }).catch(function () { toast("Clear failed", true); });
    });

    document.querySelector(".tabs").addEventListener("click", function (e) {
      var b = e.target.closest(".tab");
      if (b) selectTab(b.dataset.tab);
    });

    $("#view").addEventListener("click", function (e) {
      var traceRow = e.target.closest("[data-trace]");
      if (traceRow) { openTrace(traceRow.dataset.trace); return; }
      var logRow = e.target.closest("tr.log-row");
      if (logRow) { toggleDetailRow(logRow); return; }
    });

    $("#drawerBody").addEventListener("click", function (e) {
      var openLink = e.target.closest("[data-open-trace]");
      if (openLink) { e.preventDefault(); openTrace(openLink.dataset.openTrace); return; }
      var wf = e.target.closest(".wf-row");
      if (wf) { toggleWaterfallRow(wf); return; }
      var logRow = e.target.closest("tr.log-row");
      if (logRow) { toggleDetailRow(logRow); return; }
    });

    $("#drawer").addEventListener("click", function (e) {
      if (e.target.closest("[data-close]")) closeDrawer();
    });
    document.addEventListener("keydown", function (e) {
      if (e.key === "Escape" && !$("#drawer").hidden) closeDrawer();
    });

    // Resize only re-sizes existing charts — it never re-fetches or rebuilds the DOM,
    // which previously could feed back into another resize and loop.
    window.addEventListener("resize", debounce(resizeCharts, 200));

    ensureUplot().then(function (ok) {
      if (!ok) return;
      if (state.tab === "overview" || state.tab === "metrics") refresh(true);
    });

    state.booted = true;
    selectTab("overview");
  }

  function toggleDetailRow(row) {
    var detail = row.nextElementSibling;
    if (detail && detail.classList.contains("log-detail")) {
      detail.hidden = !detail.hidden;
    }
  }
  function toggleWaterfallRow(row) {
    var detail = row.nextElementSibling;
    if (detail && detail.classList.contains("wf-detail")) {
      detail.hidden = !detail.hidden;
      row.classList.toggle("is-open", !detail.hidden);
    }
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot);
  } else {
    boot();
  }
})();
