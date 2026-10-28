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
    for (const beast of s.beasts)
        label(badge(beast.badge, 30 + beast.might / 8, `${beast.name}, ${beast.breed}`), "badge", lift(beast.at, 0.025), 70,
            () => setHash({ panel: `why/${beast.eventId}` }));
    for (const fight of s.fights)
        label(badge(fight.badge, fight.kind === "duel" ? 26 : 34, `${fight.underWay ? "Under way" : "Fought"}: a ${fight.kind}`), "badge", lift(fight.at, 0.02), 75,
            () => setHash({ panel: `why/${fight.eventId}` }));
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

/** A realm's standard: a tall pole, a gold crown and a flag in its two colours (as the game's RealmLayer). */
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
    const screen = JSON.parse(await call("Screen", kind, key));
    if (kind === "realm") return realm(screen);
    if (kind === "character") return character(screen);
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
        + list("Stores", r.stocks, s => `${esc(s.resource[0].toUpperCase() + s.resource.slice(1))}: ${s.amount.toLocaleString("en")}`
            + (s.yearly ? ` <span class="muted">yields ${s.yearly.toLocaleString("en")} a year</span>` : "") + (s.short ? ` <b class="short">short</b>` : ""))
        + list("Council", r.council, c => `${esc(c.seat)}: ${mention(c.member)}`)
        + list("Vassals", r.vassals, mention)
        + list("Wars", r.wars, w => `${ref(w.war, w.name)} <span class="muted">(${esc(w.side)}${w.ended ? ", ended" : ""})</span>`)
        + list("Treaties", r.treaties, t => `${esc(t.kind)} with ${mention(t.with)} <span class="muted">since Year ${t.sinceYear}</span>`)
        + list("Eras", r.eras, e => `Year ${e.year}: <a data-ref="event#${e.eventId}">${esc(e.name)}</a> <span class="muted">${esc(e.phase)}</span>`);
}

function character(c) {
    return `<div class="kicker">${esc(c.kicker)}</div><h2>${esc(c.name)}</h2><p>${esc(c.line)}</p>`
        + `<div class="tags">${(c.traits ?? []).map(t => `<span>${esc(t)}</span>`).join("")}</div>`
        + `<p class="muted">${esc(c.ambition ?? "")}${c.location ? ` · ${place(c.location)}` : ""}${c.faith ? ` · ${mention(c.faith)}` : ""}</p>`
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
