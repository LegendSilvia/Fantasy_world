// The world in a browser (sub-project 11 W3). The C# viewer (FantasyWorld.Web.Viewer, the same code as the game) answers
// every question; this page only draws: the diorama with three.js, names over it, and panels for what is clicked.
// Links are hashes: #realm/3, #character/12, #why/55, #year/40.
import * as THREE from "three";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";

const call = (method, ...args) => DotNet.invokeMethodAsync("FantasyWorld.Web", method, ...args);
const $ = id => document.getElementById(id);
const say = text => { $("loading-text").textContent = text; };

const PLASTER = new THREE.Color(0xf1ebde), STONE = new THREE.Color(0xb9b1a2), INK = 0x1a1a18;

let world, scene, camera, renderer, controls, dynamic, labels = [];

// ---------- The stage: what never changes (the block, the sea, the woods) ----------

async function buildStage() {
    renderer = new THREE.WebGLRenderer({ canvas: $("world"), antialias: true });
    renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    scene = new THREE.Scene();
    scene.background = new THREE.Color(0xecebe7);
    camera = new THREE.PerspectiveCamera(32, 1, 0.01, 50);
    camera.position.set(2.2, 2.4, 2.2);
    controls = new OrbitControls(camera, renderer.domElement);
    Object.assign(controls, { enableDamping: true, minDistance: 0.35, maxDistance: 5.5, maxPolarAngle: Math.PI * 0.45, target: new THREE.Vector3(0, 0, 0) });
    scene.add(new THREE.HemisphereLight(0xffffff, 0xb8b2a6, 1.5));
    const sun = new THREE.DirectionalLight(0xffffff, 1.6);
    sun.position.set(-2, 3, 1.5);
    scene.add(sun);

    const resolution = Math.min(256, 160 + Math.round(devicePixelRatio * 32));
    for (const part of ["top", "sides"]) scene.add(new THREE.Mesh(unpack(await call("Block", part, resolution)), new THREE.MeshLambertMaterial({ vertexColors: true })));
    const water = unpack(await call("Block", "water", 96));
    scene.add(new THREE.Mesh(water, new THREE.MeshLambertMaterial({ vertexColors: true, transparent: true, depthWrite: false })));
    scene.add(trees(await call("Trees")));

    dynamic = new THREE.Group();
    scene.add(dynamic);
    addEventListener("resize", resize);
    resize();
    renderer.setAnimationLoop(frame);
}

/** A packed mesh from the viewer: counts and flags, then positions, normals, colours, alphas, uvs, indices. */
function unpack(bytes) {
    const b = bytes instanceof Uint8Array ? bytes : Uint8Array.from(atob(bytes), c => c.charCodeAt(0));
    const view = new DataView(b.buffer, b.byteOffset, b.byteLength);
    const n = view.getInt32(0, true), m = view.getInt32(4, true), flags = view.getInt32(8, true);
    let at = 12;
    const floats = count => { const out = new Float32Array(count); for (let i = 0; i < count; i++, at += 4) out[i] = view.getFloat32(at, true); return out; };
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute("position", new THREE.BufferAttribute(floats(n * 3), 3));
    geometry.setAttribute("normal", new THREE.BufferAttribute(floats(n * 3), 3));
    const rgb = floats(n * 3);
    if (flags & 1) {
        const alpha = floats(n), rgba = new Float32Array(n * 4);
        for (let i = 0; i < n; i++) rgba.set([rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2], alpha[i]], i * 4);
        geometry.setAttribute("color", new THREE.BufferAttribute(rgba, 4));
    } else geometry.setAttribute("color", new THREE.BufferAttribute(rgb, 3));
    if (flags & 2) geometry.setAttribute("uv", new THREE.BufferAttribute(floats(n * 2), 2));
    const indices = new Uint32Array(m);
    for (let i = 0; i < m; i++, at += 4) indices[i] = view.getInt32(at, true);
    geometry.setIndex(new THREE.BufferAttribute(indices, 1));
    return geometry;
}

/** Broadleaf trees as round crowns, conifers as cones, as the game draws them (eight numbers a tree). */
function trees(data) {
    const group = new THREE.Group(), count = data.length / 8;
    const kinds = [
        { geometry: new THREE.SphereGeometry(0.5, 7, 5), lift: 0.55, scale: (h) => [h * 0.95, h, h * 0.95] },
        { geometry: new THREE.ConeGeometry(0.5, 1, 6), lift: 0.5, scale: (h) => [h, h, h] },
    ];
    for (const [k, kind] of kinds.entries()) {
        const mine = [];
        for (let i = 0; i < count; i++) if (data[i * 8 + 4] === k) mine.push(i);
        const mesh = new THREE.InstancedMesh(kind.geometry, new THREE.MeshLambertMaterial(), mine.length);
        const m = new THREE.Matrix4(), c = new THREE.Color();
        mine.forEach((i, j) => {
            const [x, y, z, h] = [data[i * 8], data[i * 8 + 1], data[i * 8 + 2], data[i * 8 + 3]];
            m.compose(new THREE.Vector3(x, y + h * kind.lift, z), new THREE.Quaternion(), new THREE.Vector3(...kind.scale(h)));
            mesh.setMatrixAt(j, m);
            mesh.setColorAt(j, c.setRGB(data[i * 8 + 5], data[i * 8 + 6], data[i * 8 + 7]));
        });
        group.add(mesh);
    }
    return group;
}

// ---------- A year: what stands on the land then ----------

async function showYear(year) {
    year = Math.max(world.firstYear, Math.min(world.year, year));
    $("year").value = year;
    $("year-label").textContent = year;
    const s = JSON.parse(await call("Scene", year === world.year ? "" : String(year)));
    const pics = await pictures;
    for (const child of [...dynamic.children]) { dynamic.remove(child); child.traverse(o => o.geometry?.dispose()); }
    labels.forEach(l => l.element.remove());
    labels = [];

    dynamic.add(houses(s.houses));
    const lines = (list, material, group = dynamic) => list.forEach(points => {
        const geometry = new THREE.BufferGeometry().setAttribute("position", new THREE.BufferAttribute(Float32Array.from(points), 3));
        const line = new THREE.Line(geometry, material);
        line.computeLineDistances();
        group.add(line);
    });
    lines(s.borders, new THREE.LineBasicMaterial({ color: INK, transparent: true, opacity: 0.75 }));
    // Roads as the game draws them: close-dotted in road brown, severed ones broken and reddened; a layer of their own.
    const roads = new THREE.Group();
    roads.visible = $("roads").checked;
    roads.userData.layer = "roads";
    lines(s.roads.filter(r => r.open).map(r => r.points), new THREE.LineDashedMaterial({ color: 0x7a5a35, dashSize: 0.006, gapSize: 0.005, transparent: true, opacity: 0.7 }), roads);
    lines(s.roads.filter(r => !r.open).map(r => r.points), new THREE.LineDashedMaterial({ color: 0xa8483a, dashSize: 0.004, gapSize: 0.012, transparent: true, opacity: 0.7 }), roads);
    dynamic.add(roads);
    for (const arrow of s.arrows)
        dynamic.add(new THREE.Mesh(unpack(arrow.mesh), new THREE.MeshBasicMaterial({ vertexColors: true, transparent: true, depthTest: false, side: THREE.DoubleSide })));

    for (const realm of s.realms) {
        dynamic.add(standard(realm));
        label(`<i style="background:${css(realm.colour)}"></i><span>${esc(realm.name.toUpperCase())}<small>${esc(realm.rank)}</small></span>`,
            "realm", lift(realm.at, 0.16), 100, () => setHash({ panel: `realm/${id(realm.ref)}` }));
    }
    for (const army of s.armies) {
        dynamic.add(banner(army));
        label(esc(army.commander ? `${army.name} · ${army.commander}` : army.name), "army", lift(army.at, 0.1), 40, () => {});
    }
    // Badges: a boss marker over each beast, crossed swords where a fight was or is (a click asks why).
    const badge = (key, size, title) => `<img src="data:image/svg+xml;charset=utf-8,${encodeURIComponent(s.badges[key])}" style="height:${size}px" alt="" title="${esc(title)}">`;
    // A painted beast stands in its frame (the owner, 2026-10-29); a click opens its card, a fight's its face-off.
    const framed = (beast, size) => `<span class="beast-frame ${beast.badge}" style="width:${Math.round(size * 0.84)}px;height:${Math.round(size)}px" title="${esc(`${beast.name}, ${beast.breed}`)}">`
        + `<img src="${esc(pics[beast.ref])}" alt=""></span>`;
    for (const beast of s.beasts)
        label(pics[beast.ref] ? framed(beast, 34 + beast.might / 6) : badge(beast.badge, 30 + beast.might / 8, `${beast.name}, ${beast.breed}`), "badge", lift(beast.at, 0.025), 70,
            () => setHash({ panel: `beast/${id(beast.ref)}` }));
    for (const fight of s.fights)
        label(badge(fight.badge, fight.kind === "duel" ? 26 : 34, `${fight.underWay ? "Under way" : "Fought"}: a ${fight.kind}`), "badge", lift(fight.at, 0.02), 75,
            () => setHash({ panel: `fight/${fight.eventId}` }));
    for (const place of s.settlements)
        label(esc(place.name), place.kind === "Conflict" || place.kind === "RecentEvent" ? "news" : "", lift(place.at, 0.03),
            10 + place.size * 100, () => setHash({ panel: `settlement/${id(place.ref)}` }));
}

function houses(data) {
    const group = new THREE.Group(), count = data.length / 9;
    const walls = new THREE.InstancedMesh(new THREE.BoxGeometry(1, 1, 1), new THREE.MeshLambertMaterial(), count);
    const roofs = new THREE.InstancedMesh(new THREE.ConeGeometry(0.75, 1, 4), new THREE.MeshLambertMaterial(), count);
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), c = new THREE.Color();
    for (let i = 0; i < count; i++) {
        const [x, y, z, size, yaw, r, g, b, tower] = data.slice(i * 9, i * 9 + 9);
        q.setFromAxisAngle(new THREE.Vector3(0, 1, 0), yaw);
        const high = tower ? 1.5 : 0.7;
        m.compose(new THREE.Vector3(x, y + size * high / 2, z), q, new THREE.Vector3(size, size * high, size * 0.85));
        walls.setMatrixAt(i, m);
        walls.setColorAt(i, tower ? STONE : PLASTER);
        q.setFromAxisAngle(new THREE.Vector3(0, 1, 0), yaw + Math.PI / 4);
        m.compose(new THREE.Vector3(x, y + size * high + size * 0.25, z), q, new THREE.Vector3(size * 1.1, size * 0.5, size * 1.1));
        roofs.setMatrixAt(i, m);
        roofs.setColorAt(i, c.setRGB(r, g, b));
    }
    group.add(walls, roofs);
    return group;
}

/**
 * The world's painted images (reference to file: realms' banners, beasts, duellists), when the day's run had them; without
 * one, a flag stays plain, a beast keeps its skull badge and a card goes without a portrait.
 */
const pictures = fetch("images.json").then(r => r.ok ? r.json() : {}).catch(() => ({}));
const banners = pictures;
const bannerTextures = new Map();

/** A realm's painted banner as a texture, once loaded (null while loading or when there is none). */
function bannerTexture(url) {
    if (!bannerTextures.has(url)) {
        const texture = new THREE.TextureLoader().load(url);
        texture.colorSpace = THREE.SRGBColorSpace;
        bannerTextures.set(url, texture);
    }
    return bannerTextures.get(url);
}

/**
 * A realm's standard: a tall pole, a gold crown and a flag in its two colours (as the game's RealmLayer); where the realm
 * has a painted banner (the owner, 2026-10-29), that banner hangs from the pole instead.
 */
function standard(realm) {
    const group = new THREE.Group(), height = 0.12;
    group.position.set(...realm.at);
    const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.0016, 0.0022, height, 6), new THREE.MeshLambertMaterial({ color: 0x3d2f24 }));
    pole.position.y = height / 2;
    const crown = new THREE.Mesh(new THREE.OctahedronGeometry(0.005), new THREE.MeshStandardMaterial({ color: 0xd4af37, metalness: 0.7, roughness: 0.3 }));
    crown.position.y = height + 0.004;
    const flag = new THREE.Group();
    flag.position.set(0.026, height - 0.018, 0);
    const field = new THREE.Mesh(new THREE.PlaneGeometry(0.05, 0.032), new THREE.MeshLambertMaterial({ color: new THREE.Color(...realm.colour), side: THREE.DoubleSide }));
    const band = new THREE.Mesh(new THREE.PlaneGeometry(0.05, 0.008), new THREE.MeshLambertMaterial({ color: new THREE.Color(...realm.accent), side: THREE.DoubleSide }));
    band.position.z = 0.0004;
    flag.add(field, band);
    group.add(pole, crown, flag);
    group.userData.flag = flag;
    banners.then(list => {
        if (!list[realm.ref]) return;
        const painted = new THREE.Mesh(new THREE.PlaneGeometry(0.085, 0.125),
            new THREE.MeshBasicMaterial({ map: bannerTexture(list[realm.ref]), transparent: true, alphaTest: 0.05, side: THREE.DoubleSide }));
        painted.position.set(0.017, -0.045, 0);
        flag.clear();
        flag.add(painted);
        // A painted banner stands a head taller than a plain flag, so its crest reads from afar.
        group.scale.setScalar(1.5);
    });
    return group;
}

function banner(army) {
    const group = new THREE.Group(), height = 0.07;
    group.position.set(...army.at);
    const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.0012, 0.0016, height, 6), new THREE.MeshLambertMaterial({ color: 0x5b4636 }));
    pole.position.y = height / 2;
    const flag = new THREE.Mesh(new THREE.PlaneGeometry(0.03, 0.02), new THREE.MeshLambertMaterial({ color: new THREE.Color(...army.colour), side: THREE.DoubleSide }));
    flag.position.set(0.016, height - 0.012, 0);
    group.add(pole, flag);
    group.userData.flag = flag;
    return group;
}

// ---------- Names over the land ----------

function label(html, kind, at, priority, onClick) {
    const element = document.createElement("div");
    element.className = `label ${kind}`;
    element.innerHTML = html;
    element.addEventListener("click", onClick);
    $("labels").append(element);
    labels.push({ element, at: new THREE.Vector3(...at), priority, kind });
}

/** Places every name, the most important first, hiding any that would overlap one already placed. */
function placeLabels() {
    const w = innerWidth, h = innerHeight, taken = [], p = new THREE.Vector3();
    const distance = camera.position.distanceTo(controls.target);
    for (const l of [...labels].sort((a, b) => b.priority - a.priority)) {
        p.copy(l.at).project(camera);
        // Towns are named only once the camera comes close; realms and hosts always.
        const close = l.kind === "realm" || l.kind === "army" || l.kind === "badge" || l.priority > 60 || distance < 2.2;
        if (!close || p.z > 1 || Math.abs(p.x) > 1.05 || Math.abs(p.y) > 1.05) { l.element.style.display = "none"; continue; }
        const x = (p.x + 1) / 2 * w, y = (1 - p.y) / 2 * h;
        const box = l.kind === "badge" ? { x: x - 13, y: y - 34, w: 26, h: 32 } : { x: x - 60, y: y - 26, w: 120, h: 24 };
        if (taken.some(t => box.x < t.x + t.w && t.x < box.x + box.w && box.y < t.y + t.h && t.y < box.y + box.h)) { l.element.style.display = "none"; continue; }
        taken.push(box);
        l.element.style.display = "";
        l.element.style.left = `${x}px`;
        l.element.style.top = `${y}px`;
    }
}

function frame(time) {
    controls.update();
    for (const g of dynamic.children) if (g.userData.flag) g.userData.flag.rotation.y = Math.sin(time / 700 + g.position.x * 9) * 0.15;
    renderer.render(scene, camera);
    placeLabels();
}

function resize() {
    renderer.setSize(innerWidth, innerHeight, false);
    camera.aspect = innerWidth / innerHeight;
    camera.updateProjectionMatrix();
}

// ---------- Panels and links ----------

/** The hash holds the year and the open panel: #year/40/realm/3. */
function setHash({ year, panel }) {
    const current = parse();
    const y = year ?? current.year, open = panel === undefined ? current.panel : panel;
    location.hash = [y && y !== world.year ? `year/${y}` : "", open ?? ""].filter(Boolean).join("/");
}

function parse() {
    const parts = location.hash.replace(/^#/, "").split("/").filter(Boolean);
    let year = null;
    if (parts[0] === "year") { year = +parts[1]; parts.splice(0, 2); }
    return { year, panel: parts.length >= 2 ? `${parts[0]}/${parts[1]}` : null };
}

let shownYear = null;
async function route() {
    const { year, panel } = parse();
    const wanted = year ?? world.year;
    if (wanted !== shownYear) { shownYear = wanted; await showYear(wanted); }
    if (!panel) { $("panel").hidden = true; return; }
    const [kind, key] = panel.split("/");
    try {
        $("panel-body").innerHTML = await render(kind, key, wanted);
        $("panel").hidden = false;
        $("panel").scrollTop = 0;
    } catch (e) {
        $("panel-body").innerHTML = `<p class="muted">Nothing to show for ${esc(panel)}.</p>`;
        $("panel").hidden = false;
    }
}

$("panel-body").addEventListener("click", e => {
    const link = e.target.closest("a[data-ref]");
    if (!link) return;
    e.preventDefault();
    const [kind, number] = link.dataset.ref.split("#");
    const panel = { char: "character", realm: "realm", event: "why", artifact: "artifact", settlement: "settlement" }[kind];
    if (panel) setHash({ panel: `${panel}/${number}` });
});

async function render(kind, key, year) {
    if (kind === "settlement") {
        const map = JSON.parse(await call("Screen", "map", year === world.year ? "" : String(year)));
        const s = map.settlements.find(x => x.ref === `settlement#${key}`);
        return `<div class="kicker">${esc(s.card.kicker)}</div><h2>${esc(s.name)}</h2><p>${esc(s.card.title)}</p><p class="muted">${esc(s.card.line)}</p>`
            + (s.holder ? `<p>Held by ${ref(s.holder, "its lord")}</p>` : "");
    }
    if (kind === "gods") return gods(JSON.parse(await call("Prayers")), await tally());
    if (kind === "lore") return lore(JSON.parse(await call("Lore")));
    if (kind === "people") return people(JSON.parse(await call("People")));
    if (kind === "beast") return beastCard(JSON.parse(await call("Beast", key)), await pictures);
    if (kind === "fight") {
        const f = JSON.parse(await call("Fight", key));
        return (f ? fightCard(f, await pictures) : "") + why(JSON.parse(await call("Screen", "why", key)));
    }
    const screen = JSON.parse(await call("Screen", kind, key));
    if (kind === "realm") return realm(screen);
    if (kind === "character") return character(screen, JSON.parse(await call("Fighter", key)));
    if (kind === "why") return why(screen);
    if (kind === "artifact") return `<div class="kicker">Artifact</div><h2>${esc(screen.name ?? screen.title ?? "")}</h2><p>${esc(screen.lore ?? "")}</p>`;
    throw new Error(kind);
}

function realm(r) {
    const stat = (name, value, max = 100) => `<div><span>${name}</span><b style="width:${Math.max(2, Math.min(100, value / max * 100))}%"></b><span>${value}</span></div>`;
    return `<div class="kicker">${esc(r.rank)} · ${esc(r.government)}${r.race ? ` · ${esc(r.race)}` : ""}</div><h2>${esc(r.title)}</h2>`
        + `<p>${r.ruler ? `Ruled by ${mention(r.ruler)}` : "Without a ruler"} from ${place(r.capital)}. ${r.regions} regions.</p>`
        + `<div class="bars">${stat("Legitimacy", r.legitimacy)}${stat("Stability", r.stability)}</div>`
        + (r.liege ? `<p>Sworn to ${mention(r.liege)}.</p>` : "")
        + (r.festival ? `<p class="muted">Festival: ${esc(r.festival)}</p>` : "")
        + list("Stores", r.stocks, s => `${esc(s.resource[0].toUpperCase() + s.resource.slice(1))}: ${s.amount.toLocaleString("en")}`
            + (s.yearly ? ` <span class="muted">yields ${s.yearly.toLocaleString("en")} a year</span>` : "") + (s.short ? ` <b class="short">short</b>` : ""))
        + list("Council", r.council, c => `${esc(c.seat)}: ${mention(c.member)}`)
        + list("Vassals", r.vassals, mention)
        + list("Wars", r.wars, w => `${ref(w.war, w.name)} <span class="muted">(${esc(w.side)}${w.ended ? ", ended" : ""})</span>`)
        + list("Treaties", r.treaties, t => `${esc(t.kind)} with ${mention(t.with)} <span class="muted">since Year ${t.sinceYear}</span>`)
        + list("Eras", r.eras, e => `Year ${e.year}: <a data-ref="event#${e.eventId}">${esc(e.name)}</a> <span class="muted">${esc(e.phase)}</span>`);
}

// ---------- The players are the gods (sub-project 13 N5) ----------

/** Today's answers so far, from the site's prayers database; empty when there is none (a local copy of the site). */
async function tally() {
    try {
        const response = await fetch("api/tally", { cache: "no-store" });
        return response.ok ? await response.json() : { prayers: {}, blessings: {} };
    } catch { return { prayers: {}, blessings: {} }; }
}

const chosen = (() => { try { return JSON.parse(localStorage.getItem("fw-votes") ?? "{}"); } catch { return {}; } })();

function gods(open, counts) {
    const vote = (kind, id, choice, label, extra = "") => {
        const mine = chosen[`${kind}/${id}`] === choice ? " chosen" : "";
        return `<button class="${mine.trim()}" data-vote="${kind}" data-item="${id}" data-choice="${choice}">${extra}${label}</button>`;
    };
    const prayers = open.prayers.map(p => {
        const n = counts.prayers?.[p.id] ?? { grant: 0, deny: 0 };
        return `<div class="prayer"><p>${linked(p.text)}</p><div class="votes">`
            + vote("prayer", p.id, "grant", `Grant · ${n.grant}`) + vote("prayer", p.id, "deny", `Deny · ${n.deny}`) + `</div></div>`;
    }).join("");
    const vs = `<img class="versus" src="img/emblems/versus.png" alt="vs">`;
    const fights = open.faceOffs.map(f => {
        const n = counts.blessings?.[f.id] ?? { a: 0, b: 0 };
        const swatch = c => `<i style="background:${css(c)}"></i>`;
        return `<div class="prayer"><p>${emblem(f.a.class)}<b>${esc(f.a.name)}</b>${vs}<b>${esc(f.b.name)}</b>${emblem(f.b.class)} <span class="muted">(${esc(f.stakes)}; ${f.odds}% to ${100 - f.odds}%)</span></p><div class="votes">`
            + vote("blessing", f.id, "a", `Bless ${esc(f.a.name)} · ${n.a}`, swatch(f.a.colour))
            + vote("blessing", f.id, "b", `Bless ${esc(f.b.name)} · ${n.b}`, swatch(f.b.colour)) + `</div></div>`;
    }).join("");
    return `<div class="kicker">You are the gods</div><h2>Prayers to the gods</h2>`
        + `<p class="muted">The people of the world pray to their gods. Grant or deny each prayer, and bless a side in the fights to come: `
        + `at the next dawn the gods do as most of you chose.</p>`
        + (prayers || `<p class="muted">No one prays today.</p>`)
        + `<h3>Showdowns to come</h3>` + (fights || `<p class="muted">No fight awaits a blessing.</p>`);
}

$("gods").addEventListener("click", () => setHash({ panel: "gods/today" }));
$("lore").addEventListener("click", () => setHash({ panel: "lore/world" }));
$("people").addEventListener("click", () => setHash({ panel: "people/all" }));

/** A fight as a face-off card (the owner, 2026-10-29): both sides' portraits, classes, fates and figures, then the odds or the verdict. */
function fightCard(f, pics) {
    const moment = f.stage === "fought" ? "The verdict" : f.stage === "under way" ? "The clash rages" : "The challenge";
    const side = (x, right) => `<div class="fo-side ${right ? "right" : ""}">`
        + (pics[x.ref] ? `<div class="fo-portrait ${x.fate === "Slain" ? "slain" : ""}"><img src="${esc(pics[x.ref])}" alt="">`
            + (x.fate ? `<span class="fo-stamp ${/Victor/.test(x.fate) ? "win" : /Slain|Defeated/.test(x.fate) ? "loss" : ""}">${esc(x.fate)}</span>` : "") + `</div>` : "")
        + `<div class="fo-name"><i style="background:${css(x.colour)}"></i>${emblem(x.class)}${x.ref.startsWith("char#") || x.ref.startsWith("realm#") ? ref(x.ref, x.name) : esc(x.name)}</div>`
        + `<div class="muted">${esc([x.class && x.class[0].toUpperCase() + x.class.slice(1), ...x.notes].filter(Boolean).join(" · "))}</div>`
        + (!pics[x.ref] && x.fate ? `<div class="fo-stamp-inline">${esc(x.fate)}</div>` : "") + `</div>`;
    const rows = f.a.stats.map((x, i) => {
        const y = f.b.stats[i];
        if (!y) return "";
        const w = v => `${Math.round(100 * Math.min(1, v.value / Math.max(1, v.max)))}%`;
        return `<div class="fo-row"><span>${esc(x.shown)}</span><span class="bar left"><b style="width:${w(x)};background:${css(f.a.colour)}"></b></span>`
            + `<span class="fo-stat">${esc(x.name)}</span><span class="bar"><b style="width:${w(y)};background:${css(f.b.colour)}"></b></span><span>${esc(y.shown)}</span></div>`;
    }).join("");
    const odds = `<div class="fo-odds"><b style="width:${f.odds}%;background:${css(f.a.colour)}"></b><b style="width:${100 - f.odds}%;background:${css(f.b.colour)}"></b></div>`;
    return `<div class="kicker">${moment} · ${esc(f.stakes)}</div>`
        + `<div class="fo-sides">${side(f.a, false)}<img class="fo-vs" src="img/emblems/versus.png" alt="VS">${side(f.b, true)}</div>`
        + `<div class="fo-rows">${rows}</div>`
        + `<p class="kicker">${f.stage === "fought" ? "Victory" : "Who will prevail?"}</p>${odds}`
        + (f.result ? `<p>${esc(f.result[0].toUpperCase() + f.result.slice(1))}</p>` : `<p class="muted">By the odds, ${esc(f.words)}.</p>`)
        + `<hr>`;
}

/** A beast's card (the owner, 2026-10-29): its portrait, what it is, its legend, its lair and its deeds. */
function beastCard(b, pics) {
    if (!b) return `<p class="muted">The beast is gone from the chronicle.</p>`;
    const state = b.state === "slain" ? `Slain${b.slayer ? ` by ${ref(b.slayer.ref, b.slayer.name)}` : ""}` : b.state === "slumbering" ? "Asleep in its lair" : "Abroad and hungry";
    return `<div class="kicker">${b.legendary ? "A legend · " : ""}${esc(b.breed)}</div><h2>${esc(b.name)}</h2>`
        + (pics[b.ref] ? `<div class="beast-portrait ${b.state === "slain" ? "slain" : ""}"><img src="${esc(pics[b.ref])}" alt=""></div>` : "")
        + `<p class="muted">${state}${b.place ? ` · ${esc(b.place)}` : ""} · might ${b.might} · dread ${b.notoriety}</p>`
        + (b.lore ? `<p>${esc(b.lore)}</p>` : "")
        + (b.deeds.length ? `<h3>Its deeds</h3><ul>${b.deeds.map(d => `<li><span class="muted">Year ${d.year}</span> · ${linked(d.text)} · ${ref(d.event, "why")}</li>`).join("")}</ul>` : "");
}

/** The world's notable people, most renowned first, with a filter by name, class, realm or home (the owner, 2026-10-29). */
function people(p) {
    const row = c => `<li data-find="${esc([c.name, c.class, c.title, c.role, c.realm, c.home].filter(Boolean).join(" ").toLowerCase())}">`
        + `${emblem(c.class)}${ref(c.ref, c.name)} <span class="muted">· ${esc([c.title, c.role].filter(Boolean).join(", "))}, ${c.age}`
        + `${c.realm ? ` · ${esc(c.realm)}` : ""}${c.home ? ` · ${esc(c.home)}` : ""} · renown ${c.renown}</span></li>`;
    return `<div class="kicker">The people of the world</div><h2>Who is who</h2>`
        + `<input id="people-find" class="find" type="search" placeholder="Find by name, class, realm or home">`
        + `<ul class="people">${p.people.map(row).join("")}</ul>`;
}

$("panel-body").addEventListener("input", e => {
    if (e.target.id !== "people-find") return;
    const q = e.target.value.trim().toLowerCase();
    for (const li of $("panel-body").querySelectorAll("ul.people li")) li.hidden = q.length > 0 && !li.dataset.find.includes(q);
});

/**
 * Everything the chronicle knows (the owner, 2026-10-29): the legend, the secrets uncovered (newest first), the peoples, the
 * gods, the realms, the labyrinths (cleared, by whom), the famous relics and the shadow.
 */
function lore(l) {
    const list = (xs, f) => xs.length ? xs.map(f).join("") : "";
    const legend = l.legend ? l.legend.split(/\n+/).map(p => `<p>${esc(p)}</p>`).join("") : `<p class="muted">Its people tell no legend yet.</p>`;
    const secrets = list(l.secrets, s => `<div class="prayer"><p><b>${esc(s.title)}</b> <span class="muted">· Year ${s.year}${s.place ? ` · ${esc(s.place)}` : ""}</span></p>`
        + `<p>${esc(s.text)}</p>${s.by ? `<p class="muted">Uncovered by ${ref(s.by.ref, s.by.name)} · ${ref(s.event, "how")}</p>` : ""}</div>`);
    const races = list(l.races, r => `<div class="prayer"><p><b>The ${esc(r.name)}</b> <span class="muted">· ${esc(r.temper)}</span></p><p>${esc(r.looks.charAt(0).toUpperCase() + r.looks.slice(1))}. ${esc(r.nature)}</p>`
        + (r.foes.length || r.friends.length ? `<p class="muted">${r.foes.length ? `Old foes of the ${r.foes.map(esc).join(", ")}. ` : ""}${r.friends.length ? `Friends of the ${r.friends.map(esc).join(", ")}.` : ""}</p>` : "") + `</div>`);
    const gods = list(l.gods, g => `<li><b>${esc(g.deity)}</b>, worshipped by the ${esc(g.faith)} <span class="muted">· ${g.tenets.map(esc).join(", ")}</span></li>`);
    const realms = list(l.realms, r => `<li>${ref(r.ref, r.title)} <span class="muted">· ${r.lands} land${r.lands === 1 ? "" : "s"}</span>`
        + `${r.ruler ? ` · ruled by ${ref(r.ruler.ref, r.ruler.name)}` : ""}${r.festival ? `<br><span class="muted">Keeps ${esc(r.festival.name)}: ${esc(r.festival.rite)}</span>` : ""}</li>`);
    const mazes = list(l.labyrinths, m => `<li><b>${esc(m.name)}</b>${m.place ? ` <span class="muted">· ${esc(m.place)}</span>` : ""} · `
        + (m.cleared ? `cleared in Year ${m.cleared.year}${m.cleared.by ? ` by ${ref(m.cleared.by.ref, m.cleared.by.name)}` : ""} · ${ref(m.cleared.event, "how")}`
                     : `<span class="muted">unconquered; explored to level ${m.depth} of ${m.levels}</span>`) + `</li>`);
    const relics = list(l.relics, a => `<li>${ref(a.ref, a.name)} <span class="muted">· ${a.holder ? `held by ${esc(a.holder.name)}` : esc(a.where)}</span>${a.lore ? `<br><span class="muted">${esc(a.lore)}</span>` : ""}</li>`);
    const shadow = l.shadow ? `<p>${l.shadow.demon ? ref(l.shadow.demon.ref, l.shadow.demon.name) : "The demon lord"} `
        + (l.shadow.active ? `has risen, and the world waits for a hero.` : `was ${esc(l.shadow.outcome || "ended")}${l.shadow.hero ? ` by ${ref(l.shadow.hero.ref, l.shadow.hero.name)}` : ""}.`) + `</p>`
        : `<p class="muted">The demon lord has not yet risen.</p>`;
    const callings = list(l.callings ?? [], k => `<div class="prayer"><p><b>${esc(k.name)}</b> <span class="muted">· awakened from the ${esc(k.base)}</span></p>`
        + `<p>${esc(k.origin)}</p><p><b>Power:</b> ${esc(k.does)}. <b>Condition:</b> ${esc(k.condition)}.</p>`
        + (k.bearers.length ? `<p class="muted">Borne by ${k.bearers.map(b => ref(b.ref, b.name) + (b.living ? "" : " (fallen)")).join(", ")}${k.event ? ` · ${ref(k.event, "the first awakening")}` : ""}</p>` : "") + `</div>`);
    const part = (title, body) => body ? `<h3>${title}</h3>${body}` : "";
    return `<div class="kicker">The chronicle so far</div><h2>${esc(l.name)}</h2>`
        + part("The legend", legend)
        + `<h3>Secrets of the world</h3>` + (secrets || `<p class="muted">No secret has come to light. They say the labyrinths keep them.</p>`)
        + part("The peoples", races)
        + part("The gods", gods && `<ul class="people">${gods}</ul>`)
        + part("The realms", realms && `<ul class="people">${realms}</ul>`)
        + part("Powers awakened", callings)
        + part("The shadow", shadow)
        + part("The labyrinths", mazes && `<ul class="people">${mazes}</ul>`)
        + part("Relics of renown", relics && `<ul class="people">${relics}</ul>`);
}

$("panel-body").addEventListener("click", async e => {
    const button = e.target.closest("button[data-vote]");
    if (!button) return;
    const { vote: kind, item, choice } = button.dataset;
    button.disabled = true;
    try {
        const response = await fetch("api/vote", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ kind, item: +item, choice }) });
        if (!response.ok) throw new Error(`${response.status}`);
        chosen[`${kind}/${item}`] = choice;
        try { localStorage.setItem("fw-votes", JSON.stringify(chosen)); } catch { }
        $("panel-body").innerHTML = gods(JSON.parse(await call("Prayers")), await tally());
    } catch {
        button.disabled = false;
        button.textContent = "The gods could not hear you; try again";
    }
});

/** A character class's painted emblem (sub-project 13), with its name as the tooltip; nothing for none. */
const emblem = k => k ? `<img class="emblem" src="img/emblems/${esc(k.split(":")[0])}.png" alt="" title="${esc(k.includes(":") ? k.split(":")[1] : k[0].toUpperCase() + k.slice(1))}">` : "";

function character(c, f) {
    return `<div class="kicker">${esc(c.kicker)}${f ? ` · ${esc(f.title)}${f.awakened ? " (awakened)" : ""}` : ""}</div>`
        + `<h2>${emblem(f?.class ?? c.class)}${esc(c.name)}</h2><p>${esc(c.line)}</p>`
        + (f?.known?.length ? `<p class="known">Known as ${f.known.map(esc).join(", ")}</p>` : "")
        + (f?.calling ? `<div class="prayer"><p><b>Awakened: ${esc(f.calling.name)}</b></p><p>${esc(f.calling.origin)}</p>`
            + `<p><b>Power:</b> ${esc(f.calling.does)}. <b>Condition:</b> ${esc(f.calling.condition)}.</p></div>` : "")
        + `<div class="tags">${(c.traits ?? []).map(t => `<span>${esc(t)}</span>`).join("")}</div>`
        + `<p class="muted">${esc(c.ambition ?? "")}${c.location ? ` · ${place(c.location)}` : ""}${c.faith ? ` · ${mention(c.faith)}` : ""}</p>`
        + (f ? `<h3>In a fight</h3><p class="muted">${f.talents.map(t => `<b>${esc(t.name)}</b>: ${esc(t.shows)}`).join(" · ") || "No gift above the common."}</p>`
            + `<ul>${f.abilities.map(a => `<li><b>${esc(a.name)}</b> <span class="muted">${esc(a.text)}</span>${a.event ? ` · ${ref(a.event, "how")}` : ""}</li>`).join("")}</ul>` : "")
        + list("Ties", c.ties, t => `${esc(t.relation)}: ${ref(t.ref, t.name)}${t.qualifier ? ` <span class="muted">${esc(t.qualifier)}</span>` : ""}`)
        + list("Life", c.timeline, row => `<span class="muted">Year ${row.year}</span> ${linked(row.text)} <a data-ref="event#${row.eventId}" title="Why?">why</a>`);
}

function why(w) {
    return `<div class="kicker">How it came to this</div><h2>${linked(w.outcome.title)}</h2>`
        + (w.story ? `<p>${linked(w.story)}</p>` : "")
        + `<p class="muted">${esc(w.summary ?? "")}</p>`
        + list("People", w.people, mention) + list("Places", w.places, mention)
        + list("What followed", w.consequences, c => linked(c.title ?? c));
}

const list = (title, items, row) => items?.length ? `<h3>${title}</h3><ul>${items.map(i => `<li>${row(i)}</li>`).join("")}</ul>` : "";
const mention = m => m ? ref(m.ref, m.name) : "";
const place = p => p ? ref(p.ref, p.label ?? p.name) : "";
const ref = (r, text) => `<a data-ref="${esc(r)}">${esc(text)}</a>`;
const id = r => r.split("#")[1];
const lift = (at, y) => [at[0], at[1] + y, at[2]];
const css = rgb => `rgb(${rgb.map(v => Math.round(v * 255)).join(",")})`;
const esc = s => String(s ?? "").replace(/[&<>"']/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[ch]));

/** Linked text: each name in it follows its link. */
function linked(t) {
    if (!t) return "";
    if (typeof t === "string") return esc(t);
    let out = "", at = 0;
    for (const l of [...(t.links ?? [])].sort((a, b) => a.start - b.start)) {
        out += esc(t.text.slice(at, l.start)) + ref(l.entity, t.text.slice(l.start, l.start + l.length));
        at = l.start + l.length;
    }
    return out + esc(t.text.slice(at));
}

// ---------- Boot: last, once every helper above is defined ----------

async function boot() {
    await Blazor.start();
    try {
        say("Unrolling the map…");
        const source = new URLSearchParams(location.search).get("world") || "world.json.gz";
        const response = await fetch(source, { cache: "no-cache" });
        if (!response.ok) throw new Error(`The world could not be fetched (${response.status}).`);
        world = JSON.parse(await call("Load", new Uint8Array(await response.arrayBuffer())));
        $("name").textContent = world.name;
        const slider = $("year");
        Object.assign(slider, { min: world.firstYear, max: world.year, value: world.year, disabled: false });
        say("Raising the land…");
        await buildStage();
        slider.addEventListener("input", () => { $("year-label").textContent = slider.value; });
        slider.addEventListener("change", () => setHash({ year: +slider.value }));
        $("roads").addEventListener("change", () => dynamic.children.filter(c => c.userData.layer === "roads").forEach(c => { c.visible = $("roads").checked; }));
        window.addEventListener("hashchange", route);
        $("close").addEventListener("click", () => setHash({ panel: null }));
        await route();
        $("loading").classList.add("done");
        document.body.dataset.ready = "true";
    } catch (e) {
        say(`The world would not open: ${e.message}`);
        document.body.dataset.ready = "error";
        throw e;
    }
}

boot();
