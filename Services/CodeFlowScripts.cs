namespace vs2026_plugin.Services
{
    /// <summary>
    /// JavaScript fragments injected into the issue-details WebView2 to render
    /// the SAST CODE FLOW tab. Ported from the VS Code reference
    /// (vscode-extension/src/xygeni/service/sast-issue.ts) with two adaptations:
    ///  - vscode.postMessage(...) is replaced by chrome.webview.postMessage(JSON.stringify(...))
    ///  - var(--vscode-*) theme variables are replaced by the var(--vs-*) names emitted by GetThemeColors()
    /// The host (IssueDetailsService) wires jumpToFrame messages to IVsUIShellOpenDocument.
    /// </summary>
    internal static class CodeFlowScripts
    {
        public const string DiagramFunctions = @"
function renderDiagramInTab(containerId, nodes, links, paths) {
    const container = d3.select(containerId);
    container.selectAll('*').remove();

    function getStackIconId(d, finalNodeKeys) {
        const key = d.id + '__' + d.level;
        const type = (d.type || '').toLowerCase();
        if (finalNodeKeys.has(key)) return '#stack-bottom';
        if (d.level === 0) return '#stack-top';
        if (type.includes('sanitizer') || type.includes('propagation') || type.includes('sink')) return '#stack-middle';
        return '#stack-default';
    }

    const svg = container.append('svg')
        .attr('width','100%')
        .attr('height','100%')
        .style('display','block')
        .style('background','transparent')
        .style('cursor','grab');

    svg.on('active', () => svg.style('cursor','grabbing'));

    const g = svg.append('g');
    const defs = svg.append('defs');

    const createStackSymbol = (id, rects) => {
        const symbol = defs.append('symbol')
            .attr('id', id)
            .attr('viewBox','0 0 20 20');
        rects.forEach(r => {
            symbol.append('rect')
                .attr('x', r.x).attr('y', r.y)
                .attr('width', r.w).attr('height', r.h)
                .attr('rx', 1.5)
                .attr('fill', '#ffffff')
                .attr('opacity', r.o)
                .attr('stroke', '#e2e8f0')
                .attr('stroke-width', r.sw);
        });
    };

    createStackSymbol('stack-top', [
        {x:1,y:3,w:18,h:4,o:1.0,sw:0.4},
        {x:2,y:8,w:16,h:4,o:0.70,sw:0.6},
        {x:3,y:13,w:14,h:4,o:0.40,sw:0.8}
    ]);
    createStackSymbol('stack-middle', [
        {x:4,y:3,w:14,h:4,o:0.45,sw:0.4},
        {x:2,y:8,w:18,h:4,o:1.0,sw:0.8},
        {x:4,y:13,w:14,h:4,o:0.45,sw:0.4}
    ]);
    createStackSymbol('stack-bottom', [
        {x:3,y:3,w:14,h:4,o:0.40,sw:0.8},
        {x:2,y:8,w:16,h:4,o:0.70,sw:0.6},
        {x:1,y:13,w:18,h:4,o:1.0,sw:0.4}
    ]);
    createStackSymbol('stack-default', [
        {x:2,y:3,w:16,h:4,o:0.85,sw:0.5},
        {x:2,y:9,w:16,h:4,o:0.85,sw:0.5},
        {x:2,y:15,w:16,h:4,o:0.85,sw:0.5}
    ]);

    const nodeRadius = 30;
    const colSpacing = 220;
    const rowSpacing = 180;

    const internalNodeMap = new Map();
    nodes.forEach(d => internalNodeMap.set(d.id + '__' + d.level, Object.assign({}, d, { paths: [] })));

    paths.forEach((path, pIdx) => {
        path.forEach((id, level) => {
            const key = id + '__' + level;
            const node = internalNodeMap.get(key);
            if (!node) return;
            node.paths.push({ pathIndex: pIdx });
        });
    });

    const allNodes = Array.from(internalNodeMap.values());
    allNodes.forEach(node => {
        const avgColumn = node.paths.length ? node.paths.reduce((sum, p) => sum + p.pathIndex, 0) / node.paths.length : 0;
        node.x = avgColumn * colSpacing + 150;
        node.y = node.level * rowSpacing + 120;
    });

    const maxLevel = d3.max(allNodes, d => d.level) || 0;
    const totalHeight = (maxLevel + 1) * rowSpacing + 150;
    svg.attr('height', totalHeight);

    g.selectAll('path.link')
        .data(links)
        .enter()
        .append('path')
        .attr('fill','none')
        .attr('stroke','var(--vs-border)')
        .attr('stroke-width', 2)
        .attr('d', d => {
            const source = internalNodeMap.get(d.source);
            const target = internalNodeMap.get(d.target);
            if (!source || !target) return '';
            const controlX = source.x + Math.max(30, (target.x - source.x) / 2);
            const controlY = (source.y + target.y) / 2;
            return 'M' + source.x + ',' + source.y + ' Q' + controlX + ',' + controlY + ' ' + target.x + ',' + target.y;
        });

    g.selectAll('path.link-arrow')
        .data(links)
        .enter()
        .append('path')
        .attr('fill','var(--vs-border)')
        .attr('stroke','var(--vs-border)')
        .attr('stroke-width', 2)
        .attr('d', d => {
            const source = internalNodeMap.get(d.source);
            const target = internalNodeMap.get(d.target);
            if (!source || !target) return '';
            const controlX = source.x + Math.max(30, (target.x - source.x) / 2);
            const controlY = (source.y + target.y) / 2;
            const mx = 0.25 * source.x + 0.5 * controlX + 0.25 * target.x;
            const my = 0.25 * source.y + 0.5 * controlY + 0.25 * target.y;
            return 'M' + (mx - 7) + ',' + (my - 5) + ' L' + mx + ',' + my + ' L' + (mx - 7) + ',' + (my + 5) + ' Z';
        })
        .attr('transform', d => {
            const source = internalNodeMap.get(d.source);
            const target = internalNodeMap.get(d.target);
            if (!source || !target) return '';
            const controlX = source.x + Math.max(30, (target.x - source.x) / 2);
            const controlY = (source.y + target.y) / 2;
            const mx = 0.25 * source.x + 0.5 * controlX + 0.25 * target.x;
            const my = 0.25 * source.y + 0.5 * controlY + 0.25 * target.y;
            const angle = Math.atan2(target.y - source.y, target.x - source.x) * 180 / Math.PI;
            return 'rotate(' + angle + ', ' + mx + ', ' + my + ')';
        });

    const finalNodeKeys = new Set();
    paths.forEach(path => {
        const lastLevel = path.length - 1;
        const lastKey = path[lastLevel] + '__' + lastLevel;
        finalNodeKeys.add(lastKey);
    });

    const nodeGroups = g.selectAll('.node')
        .data(allNodes)
        .enter()
        .append('g')
        .attr('class','node')
        .attr('transform', d => 'translate(' + d.x + ', ' + d.y + ')')
        .style('cursor','pointer')
        .on('click', (event, d) => {
            chrome.webview.postMessage(JSON.stringify({
                command: 'jumpToFrame',
                file: d.filePath,
                beginLine: d.beginLine,
                beginColumn: d.beginColumn,
                endLine: d.endLine,
                endColumn: d.endColumn
            }));
        });

    nodeGroups.append('circle')
        .attr('r', nodeRadius)
        .attr('fill', d => {
            const key = d.id + '__' + d.level;
            const type = (d.type || '').toLowerCase();
            if (d.level === 0) return '#59C9A6';
            if (finalNodeKeys.has(key)) return '#1f2937';
            if (type.includes('sanitizer')) return '#f59e0b';
            if (type.includes('propagation')) return '#3b82f6';
            return '#10b981';
        })
        .attr('stroke', d => {
            const key = d.id + '__' + d.level;
            const type = (d.type || '').toLowerCase();
            if (d.level === 0) return '#4d8a7c';
            if (finalNodeKeys.has(key)) return '#111827';
            if (type.includes('propagation')) return '#4d8a7c';
            if (type.includes('sanitizer')) return '#d97706';
            return '#059669';
        })
        .attr('stroke-width', 2);

    nodeGroups.append('use')
        .attr('href', d => getStackIconId(d, finalNodeKeys))
        .attr('x', -15)
        .attr('y', -15)
        .attr('width', 30)
        .attr('height', 30)
        .style('pointer-events','none');

    g.selectAll('text.label')
        .data(allNodes.slice().sort((a, b) => a.level !== b.level ? a.level - b.level : a.x - b.x))
        .enter()
        .append('text')
        .attr('class','label')
        .attr('x', d => d.x)
        .attr('y', (d, i) => d.y + (i % 2 === 0 ? 45 : 65))
        .attr('text-anchor','middle')
        .attr('fill','var(--vs-foreground)')
        .text(d => d.label);

    const badges = g.selectAll('.badge')
        .data(allNodes.filter(d => d.paths.length > 1))
        .enter()
        .append('g')
        .attr('class','badge')
        .attr('transform', d => 'translate(' + (d.x + 15) + ', ' + (d.y - 15) + ')');

    badges.append('circle')
        .attr('r', 10)
        .attr('fill','#9e9e9e')
        .attr('stroke','white')
        .attr('stroke-width', 1);

    badges.append('text')
        .attr('text-anchor','middle')
        .attr('dy','0.3em')
        .style('font-size','10px')
        .style('font-weight','bold')
        .style('fill','white')
        .text(d => d.paths.length);

    const zoom = d3.zoom()
        .scaleExtent([0.2, 4])
        .on('zoom', event => g.attr('transform', event.transform));
    svg.call(zoom);

    const controls = container.append('div').attr('class','xy-zoom-controls');
    controls.append('button').text('+')
        .attr('class','xy-zoom-btn')
        .on('click', () => svg.transition().duration(300).call(zoom.scaleBy, 1.2));
    controls.append('button').text('-')
        .attr('class','xy-zoom-btn')
        .on('click', () => svg.transition().duration(300).call(zoom.scaleBy, 0.8));

    const tooltip = container.append('div').attr('class','tooltip');

    function escapeHtml(str) {
        if (str == null) return '';
        return String(str)
            .replace(/&/g,'&amp;')
            .replace(/</g,'&lt;')
            .replace(/>/g,'&gt;')
            .replace(/""/g,'&quot;')
            .replace(/'/g,'&#39;');
    }

    nodeGroups
        .on('mouseover', (event, d) => {
            tooltip
                .style('opacity', 1)
                .html(
                    (d.filePath ? '<strong>' + escapeHtml(d.filePath) + '</strong><br>' : '') +
                    'Line: ' + escapeHtml(d.line) + '<br>' +
                    (d.type ? 'Type: ' + escapeHtml(d.type) + '<br>' : '') +
                    (d.category ? 'Category: ' + escapeHtml(d.category) + '<br>' : '') +
                    (d.container ? 'Container: ' + escapeHtml(d.container) + '<br>' : '') +
                    (d.injectionPoint ? 'InjectionPoint: ' + escapeHtml(d.injectionPoint) + '<br>' : '') +
                    (d.code ? '<pre><code>' + escapeHtml(d.code) + '</code></pre>' : '')
                );
        })
        .on('mousemove', (event) => {
            const [mx, my] = d3.pointer(event, container.node());
            tooltip
                .style('left', (mx + 15) + 'px')
                .style('top', (my + 15) + 'px');
        })
        .on('mouseout', () => {
            tooltip.style('opacity', 0);
        });
}

function renderTextFlowInTab(containerId, nodes) {
    const container = d3.select(containerId);
    container.selectAll('*').remove();

    const steps = container.append('div')
        .attr('class','xy-text-flow-container')
        .selectAll('.xy-flow-step')
        .data(nodes.slice().sort((a, b) => a.level - b.level))
        .enter()
        .append('div')
        .attr('class','xy-flow-step')
        .on('click', (event, d) => {
            chrome.webview.postMessage(JSON.stringify({
                command: 'jumpToFrame',
                file: d.filePath,
                beginLine: d.beginLine,
                beginColumn: d.beginColumn,
                endLine: d.endLine,
                endColumn: d.endColumn
            }));
        });

    steps.each(function(d) {
        const step = d3.select(this);
        const fileName = d.filePath ? d.filePath.split(/[\\/]/).pop() : 'Unknown';

        const header = step.append('div').attr('class','xy-flow-step-header');
        header.append('span').attr('class','xy-flow-step-file').text(fileName + ':' + d.line);
        if (d.type) header.append('span').attr('class','xy-flow-step-type').text(d.type);

        step.append('div').attr('class','xy-flow-step-path').text(d.filePath || '');

        const details = step.append('div').attr('class','xy-flow-step-details');
        if (d.category) details.append('span').text('Category: ').append('b').text(d.category);
        if (d.container) details.append('span').text('Container: ').append('b').text(d.container);
        if (d.injectionPoint) details.append('span').text('InjectionPoint: ').append('b').text(d.injectionPoint);

        if (d.code) {
            step.append('pre').append('code').text(d.code);
        }
    });
}
";

        public const string MainScript = @"
(function() {
    const nodes = __NODES_PLACEHOLDER__;
    const links = __LINKS_PLACEHOLDER__;
    const paths = __PATHS_PLACEHOLDER__;
    let currentView = 'graph';

    function switchView(view) {
        currentView = view;
        const btnGraph = document.getElementById('btn-graph');
        const btnText = document.getElementById('btn-text');
        if (btnGraph) btnGraph.classList.toggle('active', view === 'graph');
        if (btnText) btnText.classList.toggle('active', view === 'text');
        render();
    }

    function render() {
        const containerId = '#code-flow-container';
        if (typeof d3 === 'undefined') return;
        if (currentView === 'graph') {
            renderDiagramInTab(containerId, nodes, links, paths);
        } else {
            renderTextFlowInTab(containerId, nodes);
        }
    }

    function initUI() {
        const btnGraph = document.getElementById('btn-graph');
        if (btnGraph) btnGraph.addEventListener('click', () => switchView('graph'));
        const btnText = document.getElementById('btn-text');
        if (btnText) btnText.addEventListener('click', () => switchView('text'));

        if (typeof d3 === 'undefined') {
            // Defer until D3 loads from CDN.
            const wait = setInterval(function() {
                if (typeof d3 !== 'undefined') {
                    clearInterval(wait);
                    switchView('graph');
                }
            }, 50);
        } else {
            switchView('graph');
        }
    }

    initUI();
})();
";
    }
}
