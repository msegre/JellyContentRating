(function () {
    'use strict';

    var lastItemId = null;
    var canEdit = false;

    // Remember the id of whatever card/element the user last clicked, since the
    // action-sheet menu itself doesn't carry the item id in its DOM.
    document.addEventListener('click', function (e) {
        var el = e.target.closest('[data-id]');
        if (el) {
            lastItemId = el.getAttribute('data-id');
        }
    }, true);

    function checkCanEdit() {
        var apiClient = window.ApiClient;
        if (!apiClient) {
            return;
        }
        fetch(apiClient.serverAddress() + '/ContentRating/CanEdit', {
            headers: { 'X-Emby-Token': apiClient.accessToken() }
        })
            .then(function (resp) { return resp.ok ? resp.json() : null; })
            .then(function (data) {
                canEdit = !!(data && data.CanEdit);
                if (canEdit) {
                    addFloatingButton();
                    scanAndBadgeCards();
                }
            })
            .catch(function () {
                canEdit = false;
            });
    }

    var BADGE_STYLE = {
        kid: { letter: 'K', color: '#2E7D32' },
        teen: { letter: 'T', color: '#1565C0' }
    };

    function findImageContainer(cardEl) {
        // Jellyfin renders poster art as a background-image on an inner div
        // rather than an <img> tag, and its exact class name has shifted across
        // versions -- so find it by the one property that's reliably true
        // (it has a background-image set) instead of guessing a class name.
        var candidates = cardEl.querySelectorAll('*');
        for (var i = 0; i < candidates.length; i++) {
            var bg = window.getComputedStyle(candidates[i]).backgroundImage;
            if (bg && bg !== 'none') {
                return candidates[i];
            }
        }
        return cardEl;
    }

    function setBadge(cardEl, tag) {
        var target = findImageContainer(cardEl);
        var existing = target.querySelector(':scope > .contentRatingBadge');
        var style = BADGE_STYLE[tag];

        if (!style) {
            if (existing) {
                existing.remove();
            }
            return;
        }

        if (!existing) {
            existing = document.createElement('div');
            existing.className = 'contentRatingBadge';
            var computedPosition = window.getComputedStyle(target).position;
            if (computedPosition === 'static') {
                target.style.position = 'relative';
            }
            target.appendChild(existing);
        }

        existing.textContent = style.letter;
        existing.style.cssText = [
            'position:absolute', 'left:4px', 'top:4px', 'z-index:20',
            'width:20px', 'height:20px', 'border-radius:50%',
            'background:' + style.color, 'color:#fff', 'font-size:12px',
            'font-weight:bold', 'display:flex', 'align-items:center',
            'justify-content:center', 'box-shadow:0 1px 4px rgba(0,0,0,0.6)',
            'pointer-events:none'
        ].join(';');
    }

    var badgeScanTimer = null;

    function scanAndBadgeCards() {
        if (!canEdit) {
            return;
        }
        var cards = document.querySelectorAll('[data-id]:not([data-content-rating-scanned])');
        if (cards.length === 0) {
            return;
        }

        var ids = [];
        cards.forEach(function (card) {
            card.setAttribute('data-content-rating-scanned', '1');
            var id = card.getAttribute('data-id');
            if (id && ids.indexOf(id) === -1) {
                ids.push(id);
            }
        });
        if (ids.length === 0) {
            return;
        }

        var apiClient = window.ApiClient;
        if (!apiClient) {
            return;
        }
        fetch(apiClient.serverAddress() + '/ContentRating/Tags?ids=' + ids.join(','), {
            headers: { 'X-Emby-Token': apiClient.accessToken() }
        })
            .then(function (resp) { return resp.ok ? resp.json() : []; })
            .then(function (tags) {
                var byId = {};
                tags.forEach(function (t) { byId[t.ItemId] = t.Tag; });
                var seen = {};
                document.querySelectorAll('[data-id]').forEach(function (card) {
                    var id = card.getAttribute('data-id');
                    // Jellyfin can nest more than one element sharing the same
                    // data-id (e.g. an outer card wrapper and an inner link) --
                    // only badge one representative element per unique id, or
                    // duplicate badges show up stacked/misplaced on hover when
                    // the extra nested element becomes visible.
                    if (id in byId && !seen[id]) {
                        seen[id] = true;
                        setBadge(card, byId[id]);
                    }
                });
            })
            .catch(function () { /* badges are cosmetic, fail silently */ });
    }

    function scheduleBadgeScan() {
        if (badgeScanTimer) {
            clearTimeout(badgeScanTimer);
        }
        badgeScanTimer = setTimeout(scanAndBadgeCards, 400);
    }

    function updateVisibleBadgesForItem(itemId, tag) {
        var el = document.querySelector('[data-id="' + itemId + '"]');
        if (el) {
            setBadge(el, tag);
        }
    }

    function showToast(message, isError) {
        var toast = document.createElement('div');
        toast.textContent = message;
        toast.style.cssText = [
            'position:fixed', 'left:50%', 'bottom:90px', 'transform:translateX(-50%)',
            'z-index:100000', 'padding:10px 18px', 'border-radius:6px',
            'background:' + (isError ? '#E53935' : '#00A4DC'), 'color:#fff',
            'font-size:14px', 'box-shadow:0 4px 10px rgba(0,0,0,0.4)',
            'transition:opacity 0.4s', 'opacity:1'
        ].join(';');
        document.body.appendChild(toast);
        setTimeout(function () {
            toast.style.opacity = '0';
            setTimeout(function () { toast.remove(); }, 400);
        }, 1800);
    }

    function setTag(itemId, tag, label) {
        var apiClient = window.ApiClient;
        if (!apiClient) {
            return;
        }
        fetch(apiClient.serverAddress() + '/ContentRating/SetTag', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Emby-Token': apiClient.accessToken()
            },
            body: JSON.stringify({ itemId: itemId, tag: tag })
        }).then(function (resp) {
            showToast(resp.ok ? ('Tagged: ' + label) : 'Failed to tag movie', !resp.ok);
            if (resp.ok) {
                updateVisibleBadgesForItem(itemId, tag);
            }
        }).catch(function () {
            showToast('Failed to tag movie (network error)', true);
        });
    }

    function openApp(query) {
        var apiClient = window.ApiClient;
        if (!apiClient) {
            return;
        }
        var url = apiClient.serverAddress() + '/ContentRating/App?token=' + encodeURIComponent(apiClient.accessToken());
        if (query) {
            url += '&q=' + encodeURIComponent(query);
        }
        window.open(url, 'contentRatingTagger', 'width=1100,height=800,noopener');
    }

    function addFloatingButton() {
        if (document.getElementById('contentRatingFab')) {
            return;
        }
        if (document.getElementById('ContentRatingConfigPage')) {
            // Don't render on the plugin's own settings page -- its fixed
            // bottom-right position could sit on top of that page's own Save
            // button and silently absorb clicks meant for it.
            return;
        }
        var btn = document.createElement('button');
        btn.id = 'contentRatingFab';
        btn.type = 'button';
        btn.title = 'Open tagging app';
        btn.textContent = '\uD83C\uDFF7\uFE0F';
        btn.style.cssText = [
            'position:fixed', 'right:20px', 'bottom:20px', 'z-index:99999',
            'width:52px', 'height:52px', 'border-radius:50%', 'border:none',
            'background:#00A4DC', 'color:#fff', 'font-size:22px',
            'box-shadow:0 4px 10px rgba(0,0,0,0.4)', 'cursor:pointer'
        ].join(';');
        btn.addEventListener('click', function () { openApp(); });
        document.body.appendChild(btn);
    }

    function buildQuickTagButton(itemId, tag, label) {
        var btn = document.createElement('button');
        btn.setAttribute('is', 'emby-button');
        btn.type = 'button';
        btn.className = 'listItem listItem-button actionSheetMenuItem emby-button contentRatingInjected';
        btn.innerHTML =
            '<span class="material-icons actionSheetItemIcon" aria-hidden="true">shield</span>' +
            '<div class="listItemBody actionsheetListItemBody">' +
            '<div class="listItemBodyText actionSheetItemText">Mark as ' + label + '</div>' +
            '</div>';
        btn.addEventListener('click', function () {
            setTag(itemId, tag, label);
        });
        return btn;
    }

    function buildOpenAppButton(itemId) {
        var btn = document.createElement('button');
        btn.setAttribute('is', 'emby-button');
        btn.type = 'button';
        btn.className = 'listItem listItem-button actionSheetMenuItem emby-button contentRatingInjected';
        btn.innerHTML =
            '<span class="material-icons actionSheetItemIcon" aria-hidden="true">open_in_new</span>' +
            '<div class="listItemBody actionsheetListItemBody">' +
            '<div class="listItemBodyText actionSheetItemText">Tag in app&hellip;</div>' +
            '</div>';
        btn.addEventListener('click', function () {
            var apiClient = window.ApiClient;
            var userId = apiClient && apiClient.getCurrentUserId ? apiClient.getCurrentUserId() : null;
            if (!apiClient || !userId || !apiClient.getItem) {
                openApp();
                return;
            }
            apiClient.getItem(userId, itemId).then(function (item) {
                openApp(item && item.Name ? item.Name : '');
            }).catch(function () {
                openApp();
            });
        });
        return btn;
    }

    function addMenuItems(sheet) {
        if (!canEdit || !lastItemId) {
            return;
        }
        if (sheet.querySelector('.contentRatingInjected')) {
            return;
        }
        var scroller = sheet.querySelector('.actionSheetScroller') || sheet.querySelector('.actionSheetContent') || sheet;
        var itemId = lastItemId;

        scroller.appendChild(buildQuickTagButton(itemId, 'kid', 'Kid'));
        scroller.appendChild(buildQuickTagButton(itemId, 'teen', 'Teen'));
        // "All" means "no kid/teen tag", not a real tag value in its own
        // right -- clicking it clears rather than writing a literal 'all'.
        scroller.appendChild(buildQuickTagButton(itemId, '', 'All'));
        scroller.appendChild(buildOpenAppButton(itemId));
    }

    function scanNode(node) {
        if (node.nodeType !== 1) {
            return;
        }
        // If the settings page appears (e.g. via client-side SPA navigation,
        // which doesn't re-run this script), remove any floating button that
        // was already added while browsing elsewhere -- it must never sit on
        // top of this page's own Save button.
        if (node.id === 'ContentRatingConfigPage' || (node.querySelector && node.querySelector('#ContentRatingConfigPage'))) {
            var fab = document.getElementById('contentRatingFab');
            if (fab) {
                fab.remove();
            }
        }
        if (node.classList && node.classList.contains('actionSheet')) {
            addMenuItems(node);
        }
        var inner = node.querySelector && node.querySelector('.actionSheet');
        if (inner) {
            addMenuItems(inner);
        }
    }

    var observer = new MutationObserver(function (mutations) {
        mutations.forEach(function (m) {
            m.addedNodes.forEach(scanNode);
        });
        scheduleBadgeScan();
    });

    function waitForApiClient(callback, attemptsLeft) {
        // window.ApiClient can exist as an object before it actually has a
        // valid access token attached -- Jellyfin's web app creates the client
        // early and attaches credentials slightly later during session
        // restore. Wait for an actual token, not just the object, or requests
        // fire with an empty/stale token and get rejected as unauthenticated.
        if (window.ApiClient && window.ApiClient.accessToken && window.ApiClient.accessToken()) {
            callback();
            return;
        }
        if (attemptsLeft <= 0) {
            return;
        }
        setTimeout(function () { waitForApiClient(callback, attemptsLeft - 1); }, 300);
    }

    function init() {
        // window.ApiClient is set up asynchronously by Jellyfin's own web app
        // bundle -- it may not exist yet at the moment this injected script
        // runs, so poll briefly instead of checking once and giving up.
        waitForApiClient(checkCanEdit, 40); // ~12s max
        observer.observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
