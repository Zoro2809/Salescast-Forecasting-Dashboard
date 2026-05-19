// Shared utilities: live autocomplete, chart rendering, history table

var MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

// ── Live Autocomplete ─────────────────────────────────────────────────────────
//
// The dropdown is appended directly to <body> and positioned with position:fixed.
// This avoids being clipped by any parent element with overflow:auto/hidden.

function initSearch(inputId, onSelect) {
    var input = document.getElementById(inputId);
    if (!input) { console.error('initSearch: no element with id "' + inputId + '"'); return; }

    // Create and attach dropdown to <body> so overflow:auto on parents cannot clip it
    var dropdown = document.createElement('div');
    dropdown.className = 'ac-drop';
    document.body.appendChild(dropdown);

    var timer     = null;
    var items     = [];
    var activeIdx = -1;

    // Position the dropdown directly below the input using viewport coordinates
    function reposition() {
        var r = input.getBoundingClientRect();
        dropdown.style.top   = (r.bottom + 4) + 'px';
        dropdown.style.left  = r.left + 'px';
        dropdown.style.width = r.width + 'px';
    }

    // ── Fetch as user types ───────────────────────────────────────────────
    input.addEventListener('input', function() {
        clearTimeout(timer);
        var q = input.value.trim();
        if (q.length < 2) {
            dropdown.style.display = 'none';
            items = []; activeIdx = -1;
            return;
        }
        reposition();
        timer = setTimeout(function() {
            fetch('/api/catalog/search?description=' + encodeURIComponent(q))
                .then(function(r) {
                    if (!r.ok) throw new Error('HTTP ' + r.status);
                    return r.json();
                })
                .then(function(data) {
                    items     = data.slice(0, 12);
                    activeIdx = -1;
                    renderItems(q);
                    reposition();
                })
                .catch(function(err) {
                    console.error('Search API error:', err);
                    dropdown.style.display = 'none';
                });
        }, 280);
    });

    // ── Keyboard navigation ───────────────────────────────────────────────
    input.addEventListener('keydown', function(e) {
        var els = dropdown.querySelectorAll('.ac-item');

        if (e.key === 'ArrowDown') {
            e.preventDefault();
            activeIdx = Math.min(activeIdx + 1, els.length - 1);
            markActive(els, activeIdx);

        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            activeIdx = Math.max(activeIdx - 1, 0);
            markActive(els, activeIdx);

        } else if (e.key === 'Enter') {
            e.preventDefault();
            if (activeIdx >= 0 && els[activeIdx]) {
                els[activeIdx].click();
            } else if (items.length > 0) {
                // Auto-pick first result
                selectItem(items[0]);
            } else {
                // Items not yet loaded — fetch now
                var q = input.value.trim();
                if (q.length >= 2) {
                    fetch('/api/catalog/search?description=' + encodeURIComponent(q))
                        .then(function(r) { return r.json(); })
                        .then(function(data) {
                            if (data.length > 0) selectItem(data[0]);
                        });
                }
            }

        } else if (e.key === 'Escape') {
            dropdown.style.display = 'none';
        }
    });

    // ── Close on outside click ────────────────────────────────────────────
    document.addEventListener('click', function(e) {
        if (e.target !== input && !dropdown.contains(e.target)) {
            dropdown.style.display = 'none';
        }
    });

    // Reposition on scroll / resize (keeps dropdown aligned if page scrolls)
    window.addEventListener('scroll', function() {
        if (dropdown.style.display === 'block') reposition();
    }, true);
    window.addEventListener('resize', function() {
        if (dropdown.style.display === 'block') reposition();
    });

    // ── Helpers ───────────────────────────────────────────────────────────
    function renderItems(query) {
        dropdown.innerHTML = '';
        if (!items.length) {
            dropdown.innerHTML = '<div class="ac-empty">No products found for "' + escHtml(query) + '"</div>';
            dropdown.style.display = 'block';
            return;
        }
        items.forEach(function(p, idx) {
            var el = document.createElement('div');
            el.className = 'ac-item';
            el.innerHTML =
                '<span class="ac-name">' + highlight(p.description, query) + '</span>' +
                '<span class="ac-price">&pound;' + fmt2(p.unitPrice) + '</span>';
            el.addEventListener('click', function() { selectItem(p); });
            dropdown.appendChild(el);
        });
        dropdown.style.display = 'block';
    }

    function selectItem(p) {
        input.value = p.description;
        dropdown.style.display = 'none';
        items = []; activeIdx = -1;
        onSelect(p);
    }

    function markActive(els, idx) {
        els.forEach(function(el, i) { el.classList.toggle('active', i === idx); });
        if (els[idx]) els[idx].scrollIntoView({ block: 'nearest' });
    }
}

// ── Utility helpers ───────────────────────────────────────────────────────────

function highlight(text, query) {
    var esc = query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return text.replace(new RegExp('(' + esc + ')', 'gi'), '<mark>$1</mark>');
}

function escHtml(s) {
    return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

function fmt2(n) { return (n != null) ? Number(n).toFixed(2) : '0.00'; }

// ── Chart Rendering ───────────────────────────────────────────────────────────

function renderHistoryChart(history, forecastValue, forecastLabel, forecastColor) {
    var labels = history.map(function(h) {
        return h.year + '-' + String(h.month).padStart(2, '0');
    });
    var units = history.map(function(h) { return h.totalUnits; });

    var traces = [{
        x: labels, y: units,
        name: 'Historical Sales',
        type: 'scatter', mode: 'lines+markers',
        line:      { color: '#94a3b8', width: 2, shape: 'spline', smoothing: 1.3 },
        marker:    { size: 6, color: '#ffffff', line: { color: '#94a3b8', width: 2 } },
        fill:      'tozeroy',
        fillcolor: 'rgba(148, 163, 184, 0.1)'
    }];

    if (forecastValue != null) {
        var last = history[history.length - 1];
        var nd   = new Date(last.year, last.month); // last.month is 1-based; new Date(y, m) gives month m (0-based) which is next month
        var nextLbl = nd.getFullYear() + '-' + String(nd.getMonth() + 1).padStart(2, '0');
        traces.push({
            x: [labels[labels.length - 1], nextLbl],
            y: [units[units.length - 1], forecastValue],
            name: forecastLabel,
            type: 'scatter', mode: 'lines+markers',
            line:   { color: forecastColor, width: 3, dash: 'dot', shape: 'spline' },
            marker: { size: 10, symbol: 'circle', color: '#ffffff', line: { color: forecastColor, width: 3 } }
        });
    }

    Plotly.newPlot('historyChart', traces, {
        margin:    { t: 20, r: 20, b: 60, l: 60 },
        legend:    { orientation: 'h', y: -0.3, x: 0, font: { color: '#64748b' } },
        xaxis:     { type: 'category', tickangle: -30, tickfont: { size: 12, color: '#94a3b8' }, gridcolor: 'rgba(0,0,0,0.03)', zeroline: false },
        yaxis:     { title: { text: 'Units', font: { color: '#94a3b8' } }, tickfont: { size: 12, color: '#94a3b8' }, gridcolor: 'rgba(0,0,0,0.03)', zerolinecolor: 'rgba(0,0,0,0.05)', rangemode: 'tozero' },
        hovermode: 'x unified',
        plot_bgcolor:  'transparent',
        paper_bgcolor: 'transparent',
        font: { family: '-apple-system,BlinkMacSystemFont,system-ui,sans-serif', size: 13, color: '#334155' }
    }, { responsive: true, displayModeBar: false });
}

// ── History Table ─────────────────────────────────────────────────────────────

function populateHistoryTable(history) {
    var tbody = document.getElementById('historyBody');
    if (!tbody) return;
    tbody.innerHTML = '';
    history.slice().reverse().forEach(function(h) {
        var tr = document.createElement('tr');
        tr.innerHTML =
            '<td>' + h.year + '</td>' +
            '<td>' + MONTHS[h.month - 1] + '</td>' +
            '<td><strong>' + (h.totalUnits != null ? h.totalUnits.toLocaleString() : '—') + '</strong></td>' +
            '<td>' + (h.avgDailyUnits != null ? h.avgDailyUnits.toFixed(1) : '—') + '</td>' +
            '<td>' + (h.maxDailyUnits != null ? h.maxDailyUnits : '—') + '</td>' +
            '<td>&pound;' + fmt2(h.avgUnitPrice) + '</td>';
        tbody.appendChild(tr);
    });
}
