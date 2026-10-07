// Typed SSE client for the routed knowledge/database pipelines.
const showSqlToggle = document.getElementById('show-sql-toggle');
if (showSqlToggle) {
    const isEnabled = localStorage.getItem('show-sql-enabled') === 'true';
    showSqlToggle.checked = isEnabled;
    document.body.classList.toggle('show-sql-enabled', isEnabled);

    showSqlToggle.addEventListener('change', () => {
        const checked = showSqlToggle.checked;
        document.body.classList.toggle('show-sql-enabled', checked);
        localStorage.setItem('show-sql-enabled', checked);
    });
}

if (window.marked && window.markedKatex) {
    window.marked.use(window.markedKatex({
        throwOnError: false,
        nonStandard: true,
        trust: false,
        strict: false
    }));
}

if (window.mermaid) {
    window.mermaid.initialize({
        startOnLoad: false,
        theme: 'dark',
        securityLevel: 'loose',
        flowchart: { useMaxWidth: false, htmlLabels: true }
    });
}

if (window.marked) {
    window.marked.use({
        renderer: {
            code(codeObj) {
                let text = '';
                let lang = '';
                if (typeof codeObj === 'object' && codeObj !== null) {
                    text = codeObj.text;
                    lang = codeObj.lang;
                } else {
                    text = arguments[0];
                    lang = arguments[1];
                }
                if (lang === 'mermaid') {
                    return `<div class="mermaid-container"><pre class="mermaid">${escapeHtml(text)}</pre></div>`;
                }
                return false;
            }
        }
    });
}

function renderRichText(text) {
    if (!window.marked || !window.DOMPurify) {
        return escapeHtml(text).replace(/\n/g, '<br>');
    }

    const rendered = window.marked.parse(text, {
        gfm: true,
        breaks: true
    });

    return window.DOMPurify.sanitize(rendered);
}

function readTableCellText(cell) {
    const clone = cell.cloneNode(true);
    clone.querySelectorAll('.katex').forEach(math => {
        const latex = math.querySelector('annotation[encoding="application/x-tex"]')?.textContent;
        math.replaceWith(document.createTextNode(latex || math.textContent || ''));
    });
    return (clone.textContent || '').replace(/\s+/g, ' ').trim();
}

function readTableMatrix(table) {
    return Array.from(table.querySelectorAll('tr')).map(row =>
        Array.from(row.querySelectorAll('th, td')).map(readTableCellText)
    );
}

function detectColumnNumberMode(values) {
    const populated = values.map(value => value.trim()).filter(Boolean);
    if (populated.length === 0) return null;
    if (populated.every(value => /^-?\d{1,3}(\.\d{3})+$/.test(value))) {
        return 'thousands-dot';
    }
    if (populated.every(value => /^-?\d{1,3}(,\d{3})+$/.test(value))) {
        return 'thousands-comma';
    }
    return null;
}

function inferSpreadsheetValue(value, columnMode = null) {
    const text = value.trim();
    if (!text) return null;
    if (/^0\d+$/.test(text)) return text;

    if (columnMode === 'thousands-dot') {
        const parsed = Number(text.replace(/\./g, ''));
        return Number.isFinite(parsed) ? parsed : text;
    }

    if (columnMode === 'thousands-comma') {
        const parsed = Number(text.replace(/,/g, ''));
        return Number.isFinite(parsed) ? parsed : text;
    }

    if (/^-?\d{1,3}(,\d{3})+(\.\d+)?$/.test(text)) {
        const parsed = Number(text.replace(/,/g, ''));
        return Number.isFinite(parsed) ? parsed : text;
    }

    if (/^-?\d+$/.test(text)) {
        const parsed = Number(text);
        return Number.isSafeInteger(parsed) ? parsed : text;
    }

    if (/^-?\d+\.\d+$/.test(text) && text.split('.')[1].length !== 3) {
        const parsed = Number(text);
        return Number.isFinite(parsed) ? parsed : text;
    }

    if (/^-?\d+(?:[.,]\d+)?%$/.test(text)) {
        const parsed = Number(text.slice(0, -1).replace(',', '.')) / 100;
        return Number.isFinite(parsed) ? parsed : text;
    }

    return text;
}

function downloadBlob(blob, fileName) {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
}

function buildExportFileName(extension, tableIndex) {
    const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
    return `WaterSupplyAI-bang-${tableIndex + 1}-${timestamp}.${extension}`;
}

async function copyTable(table, button) {
    const text = readTableMatrix(table)
        .map(row => row.join('\t'))
        .join('\n');

    if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
    } else {
        const area = document.createElement('textarea');
        area.value = text;
        area.style.position = 'fixed';
        area.style.opacity = '0';
        document.body.appendChild(area);
        area.select();
        document.execCommand('copy');
        area.remove();
    }

    const label = button.querySelector('.table-export-label');
    const oldText = label?.textContent || button.textContent;
    if (label) label.textContent = 'Đã copy';
    else button.textContent = 'Đã copy';
    setTimeout(() => {
        if (label) label.textContent = oldText;
        else button.textContent = oldText;
    }, 1400);
}

function exportTableCsv(table, tableIndex) {
    const csv = readTableMatrix(table)
        .map(row => row.map(value => `"${value.replace(/"/g, '""')}"`).join(','))
        .join('\r\n');
    downloadBlob(
        new Blob(['\uFEFF' + csv], { type: 'text/csv;charset=utf-8' }),
        buildExportFileName('csv', tableIndex)
    );
}

async function exportTableExcel(table, tableIndex, button) {
    if (!window.ExcelJS) {
        throw new Error('Không tải được thư viện xuất Excel.');
    }

    button.disabled = true;
    const label = button.querySelector('.table-export-label');
    const oldText = label?.textContent || button.textContent;
    if (label) label.textContent = 'Đang xuất...';
    else button.textContent = 'Đang xuất...';

    try {
        const matrix = readTableMatrix(table);
        const columnModes = (matrix[0] || []).map((_, columnIndex) =>
            detectColumnNumberMode(matrix.slice(1).map(row => row[columnIndex] || ''))
        );
        const workbook = new window.ExcelJS.Workbook();
        workbook.creator = 'WaterSupply AI';
        workbook.created = new Date();
        const worksheet = workbook.addWorksheet(`Bảng ${tableIndex + 1}`, {
            views: [{ state: 'frozen', ySplit: 1 }]
        });

        matrix.forEach((row, rowIndex) => {
            worksheet.addRow(row.map((value, columnIndex) =>
                rowIndex === 0 ? value : inferSpreadsheetValue(value, columnModes[columnIndex])));
        });

        const header = worksheet.getRow(1);
        header.height = 24;
        header.eachCell(cell => {
            cell.font = { bold: true, color: { argb: 'FFFFFFFF' } };
            cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF1C5D99' } };
            cell.alignment = { vertical: 'middle', horizontal: 'center' };
        });

        worksheet.eachRow((row, rowNumber) => {
            if (rowNumber === 1) return;
            row.eachCell(cell => {
                cell.alignment = {
                    vertical: 'middle',
                    horizontal: typeof cell.value === 'number' ? 'right' : 'left'
                };
                if (typeof cell.value === 'number') {
                    cell.numFmt = Number.isInteger(cell.value) ? '#,##0' : '#,##0.00';
                }
                cell.border = {
                    bottom: { style: 'thin', color: { argb: 'FFD9E2EC' } }
                };
            });
        });

        worksheet.columns.forEach(column => {
            let width = 10;
            column.eachCell({ includeEmpty: true }, cell => {
                width = Math.max(width, String(cell.value ?? '').length + 2);
            });
            column.width = Math.min(width, 40);
        });

        if (matrix.length > 1 && matrix[0]?.length > 0) {
            worksheet.autoFilter = {
                from: { row: 1, column: 1 },
                to: { row: matrix.length, column: matrix[0].length }
            };
        }

        const buffer = await workbook.xlsx.writeBuffer();
        downloadBlob(
            new Blob([buffer], {
                type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
            }),
            buildExportFileName('xlsx', tableIndex)
        );
    } finally {
        button.disabled = false;
        if (label) label.textContent = oldText;
        else button.textContent = oldText;
    }
}

const tableExportIcons = {
    copy: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="8" y="8" width="11" height="11" rx="2"></rect><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"></path></svg>',
    csv: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"></path><path d="M14 3v6h6M8 13h8M8 17h8"></path></svg>',
    excel: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4" y="4" width="16" height="16" rx="2"></rect><path d="M4 10h16M10 4v16M10 15h10"></path></svg>'
};

function closeTableExportMenus(exceptMenu = null) {
    document.querySelectorAll('.table-export-menu.is-open').forEach(menu => {
        if (menu === exceptMenu) return;
        menu.classList.remove('is-open');
        menu.querySelector('.table-export-trigger')?.setAttribute('aria-expanded', 'false');
    });
}

function createTableExportItem(label, icon, action) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'table-export-item';
    button.innerHTML = `${tableExportIcons[icon]}<span class="table-export-label">${label}</span>`;
    button.addEventListener('click', async event => {
        event.stopPropagation();
        try {
            await action(button);
        } catch (error) {
            alert(error.message);
        } finally {
            closeTableExportMenus();
        }
    });
    return button;
}

function enhanceTables(container) {
    Array.from(container.querySelectorAll('table')).forEach((table, tableIndex) => {
        if (table.closest('.table-export-container')) return;

        const wrapper = document.createElement('div');
        wrapper.className = 'table-export-container';
        const toolbar = document.createElement('div');
        toolbar.className = 'table-export-toolbar';

        const menu = document.createElement('div');
        menu.className = 'table-export-menu';

        const trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.className = 'table-export-trigger';
        trigger.setAttribute('aria-label', 'Mở menu thao tác với bảng');
        trigger.setAttribute('aria-haspopup', 'menu');
        trigger.setAttribute('aria-expanded', 'false');
        trigger.textContent = '•••';

        const panel = document.createElement('div');
        panel.className = 'table-export-panel';
        panel.setAttribute('role', 'menu');

        const copyButton = createTableExportItem('Copy', 'copy', button =>
            copyTable(table, button));
        const csvButton = createTableExportItem('CSV', 'csv', () =>
            exportTableCsv(table, tableIndex));
        const excelButton = createTableExportItem('Excel', 'excel', button =>
            exportTableExcel(table, tableIndex, button));

        trigger.addEventListener('click', event => {
            event.stopPropagation();
            const willOpen = !menu.classList.contains('is-open');
            closeTableExportMenus(menu);
            menu.classList.toggle('is-open', willOpen);
            trigger.setAttribute('aria-expanded', String(willOpen));
            if (willOpen) panel.querySelector('button')?.focus();
        });

        panel.append(copyButton, csvButton, excelButton);
        menu.append(trigger, panel);
        toolbar.append(menu);

        const scrollWrapper = document.createElement('div');
        scrollWrapper.className = 'table-scroll-wrapper';

        table.parentNode.insertBefore(wrapper, table);
        scrollWrapper.append(table);
        wrapper.append(toolbar, scrollWrapper);
    });
}

const imageUpload = document.getElementById('image-upload');
const imagePreviewContainer = document.getElementById('image-preview-container');
const imagePreview = document.getElementById('image-preview');
const clearImageBtn = document.getElementById('clear-image-btn');
let selectedImageBase64 = null;

function handleImageFile(file) {
    if (!file) return;

    const reader = new FileReader();
    reader.onload = e => {
        const img = new Image();
        img.onload = () => {
            const maxDim = 1024;
            let width = img.width;
            let height = img.height;
            if (width > maxDim || height > maxDim) {
                if (width > height) {
                    height = Math.round((height * maxDim) / width);
                    width = maxDim;
                } else {
                    width = Math.round((width * maxDim) / height);
                    height = maxDim;
                }
            }

            const canvas = document.createElement('canvas');
            canvas.width = width;
            canvas.height = height;
            const ctx = canvas.getContext('2d');
            ctx.drawImage(img, 0, 0, width, height);

            const compressedBase64 = canvas.toDataURL('image/jpeg', 0.8);
            selectedImageBase64 = compressedBase64.split(',')[1];

            if (imagePreview) imagePreview.src = compressedBase64;
            if (imagePreviewContainer) imagePreviewContainer.style.display = 'flex';
        };
        img.src = e.target.result;
    };
    reader.readAsDataURL(file);
}

if (imageUpload) {
    imageUpload.addEventListener('change', event => {
        const file = event.target.files[0];
        if (file) {
            handleImageFile(file);
        }
        imageUpload.value = '';
    });
}

if (userInput) {
    userInput.addEventListener('paste', event => {
        const items = (event.clipboardData || window.clipboardData)?.items;
        if (!items) return;
        for (const item of items) {
            if (item.type.indexOf('image') === 0) {
                const file = item.getAsFile();
                if (file) {
                    event.preventDefault();
                    handleImageFile(file);
                    break;
                }
            }
        }
    });
}

if (clearImageBtn) {
    clearImageBtn.addEventListener('click', () => {
        selectedImageBase64 = null;
        if (imagePreview) imagePreview.src = '';
        if (imagePreviewContainer) imagePreviewContainer.style.display = 'none';
    });
}

document.addEventListener('click', () => closeTableExportMenus());
document.addEventListener('keydown', event => {
    if (event.key === 'Escape') closeTableExportMenus();
});

window.askQuestionV2 = async function () {
    const question = userInput.value.trim();
    const imagePayload = selectedImageBase64;
    if (!question && !imagePayload) return;

    appendMessage('user', question, imagePayload);
    userInput.value = '';
    if (clearImageBtn) {
        clearImageBtn.click();
    }
    sendBtn.disabled = true;

    const aiMsg = appendMessage('ai', '');
    let statusText = 'Đang xử lý...';
    let answerText = '';
    let sqlQuery = '';
    let routeIntent = '';
    let turnContext = {};
    const agentTrace = [];
    let agentStepsCount = 0;
    let agentMaxSteps = 10;
    let agentToolCallsCount = 0;
    let agentCompleted = false;

    const render = () => {
        let html = '';

        if (routeIntent) {
            html += `<div style="opacity:.6;font-size:12px;margin-bottom:10px">${escapeHtml(routeIntent)}</div>`;
        }

        if (sqlQuery) {
            html += '<details class="sql-details">' +
                '<summary>🔍 Xem câu lệnh SQL</summary>' +
                `<pre><code>${escapeHtml(sqlQuery)}</code></pre>` +
                '</details>';
        }

        if (agentTrace.length > 0) {
            let summaryText = 'Quy trình Agent';
            if (agentCompleted) {
                summaryText += ` (Hoàn thành - ${agentStepsCount} bước, ${agentToolCallsCount} lần gọi tool)`;
            } else if (agentStepsCount > 0) {
                summaryText += ` (Bước ${agentStepsCount}/${agentMaxSteps})`;
            }

            html += '<details class="sql-details" open>' +
                `<summary>${escapeHtml(summaryText)}</summary>` +
                '<div style="padding:10px 0;font-size:12px;opacity:.75">' +
                agentTrace.map(item => `<div style="margin:4px 0">${escapeHtml(item)}</div>`).join('') +
                '</div></details>';
        }

        if (answerText) {
            html += renderRichText(answerText);
        } else {
            html += `<span class="typing">${escapeHtml(statusText)}</span>`;
        }

        aiMsg.innerHTML = html;
        enhanceTables(aiMsg);
        chatHistoryContainer.scrollTop = chatHistoryContainer.scrollHeight;
    };

    const handleEvent = (eventName, payload) => {
        switch (eventName) {
            case 'status':
                statusText = payload.message || 'Đang xử lý...';
                break;
            case 'route':
                const confidence = Number.isFinite(payload.confidence)
                    ? `${Math.round(payload.confidence * 100)}%`
                    : '?';
                const elapsed = Number.isFinite(payload.elapsedMs)
                    ? `${payload.elapsedMs} ms`
                    : '?';
                routeIntent = `Loại yêu cầu: ${payload.intent} · ${payload.source || 'UNKNOWN'} · ${confidence} · ${elapsed}`;
                const previousAssistantContext = [...historyData]
                    .reverse()
                    .find(message => message.role === 'assistant' && message.context)
                    ?.context || {};
                const shouldCarryPreviousContext = payload.action === 'ANSWER_FROM_HISTORY' &&
                    previousAssistantContext.intent === (payload.semanticIntent || payload.intent);
                turnContext = {
                    ...(shouldCarryPreviousContext ? previousAssistantContext : turnContext),
                    action: payload.action || 'ROUTE',
                    intent: payload.semanticIntent || payload.intent || '',
                    resolvedQuestion: payload.resolvedQuestion || ''
                };
                break;
            case 'context':
                turnContext = { ...turnContext, ...payload };
                break;
            case 'sql':
                sqlQuery = payload.query || '';
                break;
            case 'agent_step':
                agentStepsCount = payload.step;
                agentMaxSteps = payload.maxSteps || agentMaxSteps;
                statusText = `Agent bước ${payload.step}/${payload.maxSteps}: ${payload.status || 'Đang xử lý...'}`;
                agentTrace.push(`Bước ${payload.step}/${payload.maxSteps}: ${payload.status || 'Đang xử lý...'}`);
                break;
            case 'tool_call':
                agentToolCallsCount++;
                statusText = `Đang gọi tool ${payload.tool}...`;
                agentTrace.push(`→ Gọi tool: ${payload.tool}`);
                break;
            case 'tool_result':
                statusText = payload.summary || 'Đã nhận kết quả tool.';
                agentTrace.push(`${payload.success ? '✓' : '✗'} ${payload.tool}: ${payload.summary || ''}`);
                break;
            case 'agent_done':
                agentCompleted = true;
                agentStepsCount = payload.steps;
                agentToolCallsCount = payload.toolCalls;
                agentTrace.push(`Hoàn tất sau ${payload.steps} bước, ${payload.toolCalls} lần gọi tool, ${payload.elapsedMs} ms`);
                break;
            case 'answer_delta':
                answerText += payload.text || '';
                break;
            case 'error':
                throw new Error(payload.message || 'Không thể xử lý yêu cầu.');
        }

        render();
    };

    render();

    try {
        const endpoint = '/api/ask-v2?debug=1';
        const response = await fetch(endpoint, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ 
                question, 
                history: historyData,
                images: imagePayload ? [imagePayload] : null
            })
        });

        if (!response.ok || !response.body) {
            throw new Error(`Server trả về lỗi ${response.status}`);
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        while (true) {
            const { value, done } = await reader.read();
            buffer += decoder.decode(value || new Uint8Array(), { stream: !done });

            let boundary;
            while ((boundary = buffer.indexOf('\n\n')) >= 0) {
                const block = buffer.slice(0, boundary);
                buffer = buffer.slice(boundary + 2);

                let eventName = 'message';
                const dataLines = [];
                for (const line of block.split('\n')) {
                    if (line.startsWith('event:')) {
                        eventName = line.slice(6).trim();
                    } else if (line.startsWith('data:')) {
                        dataLines.push(line.slice(5).trimStart());
                    }
                }

                if (dataLines.length > 0) {
                    handleEvent(eventName, JSON.parse(dataLines.join('\n')));
                }
            }

            if (done) break;
        }

        if (window.mermaid) {
            try {
                await window.mermaid.run({ querySelector: '.mermaid' });
            } catch (err) {
                console.error("Lỗi vẽ sơ đồ Mermaid:", err);
            }
        }

        historyData.push({ role: 'user', content: question });
        historyData.push({ role: 'assistant', content: answerText, context: turnContext });
    } catch (error) {
        aiMsg.innerHTML = `<span style="color:#ff6b6b">❌ ${escapeHtml(error.message)}</span>`;
    } finally {
        sendBtn.disabled = false;
        userInput.focus();
    }
};

askForm.onsubmit = event => {
    event.preventDefault();
    window.askQuestionV2();
};
