import L from 'leaflet';
import { useEffect, useRef } from 'react';
import { MapContainer, useMap, ZoomControl } from 'react-leaflet';
import 'leaflet/dist/leaflet.css';
import type { PrefectureFeature } from '../api/types';
import { COVERAGE, type Coverage } from '../data/coverage';
import type { PrefectureMapProps } from './PrefectureMap';
import './leaflet-map.css';

/**
 * The Leaflet implementation of {@link PrefectureMapProps}. This is the only file
 * in the app that imports `leaflet` or `react-leaflet`.
 *
 * ## No tile layer
 *
 * There is no basemap underneath the polygons, and that is deliberate rather than
 * unfinished. A tile layer would pull raster images from a third-party server on
 * every pan, which means an external runtime dependency, a second attribution
 * requirement, and a source that is not listed in DATA-SOURCES.md — and that file
 * governs everything this project displays. The map draws our own PostGIS
 * geometry on a plain ground, which is also what a choropleth wants: nothing
 * competing with the fills that carry the data.
 *
 * ## Imperative layers inside a declarative container
 *
 * `MapContainer` is react-leaflet, the layers are raw Leaflet. react-leaflet's
 * `<GeoJSON>` reads its `style` prop once and does not re-read it, so restyling on
 * selection would mean remounting the component and rebuilding all 47
 * multipolygons on every tap. Holding the layers and calling `setStyle` on the one
 * that changed is both faster and a plainer description of what happens.
 */
export function LeafletPrefectureMap(props: PrefectureMapProps) {
  return (
    <MapContainer
      className="leaflet-map"
      // Initial view. Not the bounding box of the data: Tokyo's prefecture
      // includes the Ogasawara and Minamitorishima islands, which reach 154°E and
      // 24°N, so fitting to the data would put the whole archipelago in a corner
      // of an ocean. These bounds cover Yonaguni to eastern Hokkaidō, which is
      // the map people mean by "Japan".
      bounds={INITIAL_BOUNDS}
      boundsOptions={INITIAL_BOUNDS_OPTIONS}
      // Panning is allowed well past the geometry. See PANNABLE_BOUNDS.
      maxBounds={PANNABLE_BOUNDS}
      maxBoundsViscosity={MAX_BOUNDS_VISCOSITY}
      // Leaflet rounds the zoom that fits `bounds` down to a multiple of
      // `zoomSnap`. With the default of 1, a pane that is almost tall enough for
      // zoom 6 opens at zoom 5, where Japan is half the height the pane can show.
      // Japan is taller than it is wide, so the pane height sets this zoom and
      // extra width only adds sea. A step of 0.1 fills the height to within 7%.
      zoomSnap={ZOOM_SNAP}
      minZoom={MIN_ZOOM}
      maxZoom={MAX_ZOOM}
      // The footer carries the MLIT attribution in full, including the
      // processing declaration its terms require. That text is far longer than a
      // corner control, so Leaflet's own attribution control is turned off rather
      // than left showing a shorter, non-compliant version of the same thing.
      attributionControl={false}
      // Leaflet's default zoom control is at the top left, under the legend. The
      // <ZoomControl> below puts the same buttons at the top right instead.
      zoomControl={false}
      renderer={RENDERER}
      // Leaflet's default is to require ctrl/cmd with the wheel only when
      // scroll-through is a risk. The map fills its own pane and the page does not
      // scroll behind it, so a plain wheel zoom is not a trap here.
      scrollWheelZoom
      aria-label="Choropleth map of Japan's 47 prefectures, coloured by how complete their mascot data is. The prefecture list beside it carries the same information as text."
    >
      {/* Shown at every width. Pinch zoom needs two fingers, and WCAG 2.5.1
          requires a one-pointer way to do the same thing: double-tap zooms in,
          and nothing else zooms out on a touch screen. The top right, because
          the legend is at the top left and the bottom sheet opens at the
          bottom. */}
      <ZoomControl position="topright" />
      <PrefectureLayers {...props} />
      <SizeWatcher />
      <ClickTolerance />
    </MapContainer>
  );
}

/** Yonaguni in the south-west to eastern Hokkaidō in the north-east. */
const INITIAL_BOUNDS: L.LatLngBoundsExpression = [
  [24.0, 122.9],
  [45.7, 146.1]
];

/**
 * A margin inside the map pane around {@link INITIAL_BOUNDS}, in pixels. With a
 * zoom step of 0.1 the fit is exact, so without a margin Yonaguni and eastern
 * Hokkaidō touch the edges of the pane and look cut off.
 */
const INITIAL_BOUNDS_OPTIONS: L.FitBoundsOptions = { padding: [16, 16] };

/**
 * How far the map may be panned. Two things set it.
 *
 * Tokyo's far islands reach 24°N and 154°E, and must be reachable.
 *
 * A selected prefecture's label is centred in the map, and Leaflet stops a pan
 * at these bounds. At the zoom that fits Japan on a 375px phone, about 10 pixels
 * to a degree, centring Okinawa's label above the bottom sheet needs the view to
 * reach about 25° south of it and 20° west of it; centring Hokkaidō's needs about
 * as much to the north and east. Bounds closer to the geometry leave those
 * labels at an edge or under the sheet. The cost is that a drag can go further
 * out into empty sea before it stops.
 */
const PANNABLE_BOUNDS: L.LatLngBoundsExpression = [
  [0, 100],
  [65, 170]
];

/**
 * The one SVG renderer for every prefecture layer.
 *
 * `padding` is how far outside the visible map area Leaflet draws, as a fraction
 * of the map size on each side. Leaflet draws again only when a drag ends, so
 * with its default of 0.1 a drag longer than a tenth of the map shows empty
 * sea where prefectures should be, and then every shape appears at once. At 1,
 * Leaflet draws one map width to each side and one map height above and below,
 * so a drag of up to a full map width shows no empty area.
 */
const RENDERER = L.svg({ padding: 1 });

/**
 * Stroke width of the invisible hit layer, in pixels. Roughly a fingertip either
 * side of the border.
 *
 * This is the CLAUDE.md requirement that small prefectures stay tappable. Kagawa
 * is about 12 pixels across at the zoom that fits Japan on a phone, which is
 * below the ~44px minimum touch target; a 20px stroke centred on its border gives
 * it a target about 32px across without changing what is drawn.
 */
const HIT_STROKE_WEIGHT = 20;

/** Selection outline. Warm orange, so it cannot be read as a coverage state. */
const SELECTED_STROKE = '#c2410c';

/**
 * How far out and in the visitor may zoom.
 *
 * The lower bound stops the map reaching a state where Japan is a few pixels
 * across and nothing can be tapped. The upper bound is past any detail this data
 * holds: at `low` the geometry is simplified to 0.02°, roughly 1.8 km, so
 * magnifying further only shows that the coastline has become straight lines.
 * See DetailLevel.cs.
 */
const MIN_ZOOM = 3;
const MAX_ZOOM = 10;

/**
 * The smallest zoom step. `zoomDelta` stays at its default of 1, so a double
 * click and the keyboard + and - keys still change the zoom by one whole level.
 */
const ZOOM_SNAP = 0.1;

/**
 * How firmly the map resists a drag past {@link PANNABLE_BOUNDS}. 0 lets it move
 * freely and spring back on release; 1 is a hard edge the drag cannot cross.
 * Just under 1 leaves the edge slightly soft, so a drag there still moves the map
 * a little rather than appearing to be ignored.
 */
const MAX_BOUNDS_VISCOSITY = 0.9;

/**
 * How far inside the map edges, in pixels, a selected prefecture's label must be
 * to count as in view. A label closer to an edge than this is centred instead.
 * The strip that `obscuredBottomPx` returns is added at the bottom, so a label under the bottom sheet
 * also counts as out of view.
 */
const SELECTION_PADDING = 24;

/**
 * Duration and easing of a short pan to a selection. Leaflet eases out with a
 * power of 1 / easeLinearity: its default of 0.25 is a 4th-power curve that does
 * most of the move in the first frames, which reads as a jump. 0.5 is a 2nd-power
 * curve.
 */
const PAN_DURATION_S = 0.6;
const PAN_EASE_LINEARITY = 0.5;

/**
 * Fill opacity and stroke weight, unselected and selected.
 *
 * A selection changes three things at once — the stroke colour to
 * {@link SELECTED_STROKE}, the stroke weight, and the fill to full opacity — so
 * it stays visible to a reader who cannot separate that orange from the three
 * coverage fills.
 */
const FILL_OPACITY_DEFAULT = 0.85;
const FILL_OPACITY_SELECTED = 1;
const STROKE_WEIGHT_DEFAULT = 0.6;
const STROKE_WEIGHT_SELECTED = 2.5;

function PrefectureLayers({
  collection,
  coverageFor,
  selectedJisCode,
  onSelect,
  obscuredBottomPx
}: PrefectureMapProps) {
  const map = useMap();

  // The drawn polygon for each prefecture, so a selection change restyles two
  // layers instead of rebuilding the collection.
  const shapesRef = useRef(new Map<number, L.GeoJSON>());
  // The name label for each prefecture, and where it sits.
  const labelsRef = useRef(new Map<number, L.Marker>());
  const labelPointsRef = useRef(new Map<number, L.LatLng>());
  // The layer group of invisible hit targets, taken off the map during a flight.
  const hitLayerRef = useRef<L.LayerGroup | null>(null);

  // Read by handlers that are attached once, when the layers are built. Without
  // this the first render's callbacks would be the ones still running after a
  // selection changed.
  const onSelectRef = useRef(onSelect);
  const coverageForRef = useRef(coverageFor);
  const selectedRef = useRef(selectedJisCode);

  useEffect(() => {
    onSelectRef.current = onSelect;
    coverageForRef.current = coverageFor;
    selectedRef.current = selectedJisCode;
  });

  // Set when the layers are built, so the selection effect can ask the labels to
  // be placed again without duplicating the rule for which of them are shown.
  const placeLabelsRef = useRef<(() => void) | null>(null);

  // Build the layers. Runs on mount and again when the collection is replaced,
  // which happens when the viewport crosses the breakpoint and the app refetches
  // at a different detail level.
  useEffect(() => {
    const shapes = shapesRef.current;
    const labels = labelsRef.current;
    const labelPoints = labelPointsRef.current;

    const shapeLayer = L.layerGroup().addTo(map);
    const hitLayer = L.layerGroup().addTo(map);
    hitLayerRef.current = hitLayer;
    const labelLayer = L.layerGroup().addTo(map);

    // Largest first, so the smallest prefectures are last in the SVG and their
    // hit strokes sit on top of their neighbours'. A 20px stroke reaches well
    // outside its own prefecture, and without this ordering Hyōgo's stroke would
    // cover a good part of the target this technique exists to give Ōsaka.
    const features = [...collection.features].sort(
      (left, right) => approximateArea(right) - approximateArea(left)
    );

    // Read on every click on the hit layer, so the boxes are computed once here.
    const outlines = features.map((feature) => ({ feature, box: boundingBox(feature) }));

    for (const feature of features) {
      const { jisCode, nameJa, labelLat, labelLng } = feature.properties;

      const shape = L.geoJSON(feature, {
        style: shapeStyle(coverageForRef.current(jisCode), selectedRef.current === jisCode),
        // All events belong to the hit layer below. Leaving this interactive too
        // would mean two layers competing for the same tap.
        interactive: false
      });

      shapeLayer.addLayer(shape);
      shapes.set(jisCode, shape);

      // The hit layer: the same geometry, drawn with a wide stroke and a fill
      // that are both fully transparent. Transparency does not remove a shape
      // from hit testing — SVG decides that from whether `fill` and `stroke` are
      // set at all, not from their opacity — so this is a target roughly 20px
      // wider than the prefecture, and invisible.
      const hit = L.geoJSON(feature, {
        style: {
          stroke: true,
          color: '#000000',
          opacity: 0,
          weight: HIT_STROKE_WEIGHT,
          // Round joins stop the stroke spiking out into long points at the sharp
          // corners of a coastline, which would put a prefecture's target
          // kilometres out to sea.
          lineJoin: 'round',
          lineCap: 'round',
          fill: true,
          fillOpacity: 0
        },
        interactive: true
      });

      hit.on('click', (event: L.LeafletMouseEvent) => {
        // Otherwise the click continues to the map and the background handler
        // below immediately clears the selection this one just made.
        L.DomEvent.stopPropagation(event);
        // The wide stroke of a smaller prefecture is on top of its neighbours, so
        // the layer that received the click is not always the prefecture under
        // the pointer. A click inside a prefecture's real shape selects that
        // prefecture. The layer decides only for a click outside every shape,
        // which is the case the wide stroke exists for.
        onSelectRef.current(prefectureAt(outlines, event.latlng) ?? jisCode);
      });

      hitLayer.addLayer(hit);

      // The label, drawn at the stored ST_PointOnSurface. That point is always
      // inside the polygon; a centroid is not, for the four prefectures whose
      // shape is concave or mostly islands. See DECISIONS.md 5.
      if (labelLat !== null && labelLng !== null) {
        const point = L.latLng(labelLat, labelLng);
        labelPoints.set(jisCode, point);

        // Built as an element with textContent rather than an HTML string, so
        // the name is text and never markup.
        const text = document.createElement('span');
        text.className = 'leaflet-map__label-text';
        // The Japanese name, because it is the compact one: 北海道 is three
        // characters where "Hokkaidō" is eight, and at this scale the difference
        // decides whether neighbouring labels collide. The list view and the
        // detail panel carry the English and romaji names.
        text.textContent = nameJa;

        labels.set(
          jisCode,
          L.marker(point, {
            icon: L.divIcon({
              html: text,
              className: 'leaflet-map__label',
              // Zero size with a zero anchor puts the container div exactly on
              // the point; the span inside is centred on it by CSS.
              iconSize: [0, 0],
              iconAnchor: [0, 0]
            }),
            interactive: false,
            keyboard: false
          })
        );
      }
    }

    // A click that no hit layer received. Tapping the sea clears the selection,
    // which is how a bottom sheet is dismissed without a close button being the
    // only way. The click can also be inside a prefecture: when the press and the
    // release are on two different hit layers, the browser sends the click to
    // the element that contains both, and Leaflet passes it to the map. The
    // prefecture under the release point is selected then.
    const selectAtPoint = (event: L.LeafletMouseEvent) =>
      onSelectRef.current(prefectureAt(outlines, event.latlng));
    map.on('click', selectAtPoint);

    // Each label's size in pixels, measured once while every label is attached.
    // A detached label has no layout, so it cannot be measured later. The font
    // size is fixed, so the sizes stay correct until the layers are built again.
    const labelSizes = measureLabels(labels, labelLayer);

    // Labels are placed by collision, as Google Maps, Mapbox GL and MapLibre
    // place theirs: in priority order, skipping any label whose box overlaps a
    // label already placed. The selected prefecture has the highest priority, so
    // a tap always names what it selected. After it, the order is `features`:
    // largest prefecture first, because a large prefecture has room around its
    // label and a small one is in the list either way.
    const labelOrder = features.map((feature) => feature.properties.jisCode);

    const placeLabels = () => {
      const selected = selectedRef.current;
      const order =
        selected === null
          ? labelOrder
          : [selected, ...labelOrder.filter((jisCode) => jisCode !== selected)];
      const gapPx = LABEL_GAP_REM * rootFontSizePx();
      const placed: LabelBox[] = [];

      for (const jisCode of order) {
        const marker = labels.get(jisCode);
        const point = labelPoints.get(jisCode);
        const size = labelSizes.get(jisCode);

        if (!marker || !point || !size) {
          continue;
        }

        // Layer points, not container points: both change by the same amount
        // when the map is panned, so the result depends on the zoom only.
        const centre = map.latLngToLayerPoint(point);
        const box: LabelBox = {
          left: centre.x - size.width / 2 - gapPx / 2,
          right: centre.x + size.width / 2 + gapPx / 2,
          top: centre.y - size.height / 2 - gapPx / 2,
          bottom: centre.y + size.height / 2 + gapPx / 2
        };

        const visible = !placed.some((other) => overlaps(box, other));
        const attached = labelLayer.hasLayer(marker);

        if (visible) {
          placed.push(box);
        }

        if (visible && !attached) {
          labelLayer.addLayer(marker);
        } else if (!visible && attached) {
          labelLayer.removeLayer(marker);
        }
      }
    };

    placeLabels();
    // zoomend only. A pan moves every label by the same amount, so it cannot
    // make two labels overlap or stop overlapping.
    map.on('zoomend', placeLabels);
    placeLabelsRef.current = placeLabels;

    return () => {
      map.off('click', selectAtPoint);
      map.off('zoomend', placeLabels);
      placeLabelsRef.current = null;
      hitLayerRef.current = null;
      map.removeLayer(shapeLayer);
      map.removeLayer(hitLayer);
      map.removeLayer(labelLayer);
      shapes.clear();
      labels.clear();
      labelPoints.clear();
    };
  }, [map, collection]);

  // Restyle on a selection or coverage change. Two setStyle calls in the common
  // case — the prefecture being deselected and the one being selected.
  useEffect(() => {
    for (const [jisCode, shape] of shapesRef.current) {
      shape.setStyle(shapeStyle(coverageFor(jisCode), selectedJisCode === jisCode));
    }

    // The selection outline is centred on the border, so a neighbour drawn after
    // the selected shape covers the inner half of the outline along their shared
    // border. Moving the selected shape to the end of the SVG draws its whole
    // outline on top. The shape takes no pointer events, so this does not change
    // which hit layer receives a click.
    if (selectedJisCode !== null) {
      shapesRef.current.get(selectedJisCode)?.bringToFront();
    }

    placeLabelsRef.current?.();
  }, [coverageFor, selectedJisCode]);

  // Bring a selection into view. This is what makes a tap in the list view move
  // the map, which is the whole reason the two views are worth showing together.
  useEffect(() => {
    if (selectedJisCode === null) {
      return;
    }

    const point = labelPointsRef.current.get(selectedJisCode);

    if (!point) {
      return;
    }

    const covered = obscuredBottomPx;

    // The part of the map a label must be inside to count as in view: the map
    // less a margin, and less the strip the bottom sheet covers.
    const size = map.getSize();
    const inView = L.bounds(
      [SELECTION_PADDING, SELECTION_PADDING],
      [size.x - SELECTION_PADDING, size.y - SELECTION_PADDING - covered]
    ).contains(map.latLngToContainerPoint(point));

    if (inView) {
      return;
    }

    // The label is off screen, which also happens after a click on the edge of a
    // prefecture that is larger than the map. The map centres on the label at the
    // current zoom, so the visitor sees which prefecture was selected. The label
    // goes to the centre of the
    // part of the map the sheet does not cover, which is half the sheet height
    // above the map's own centre.
    const zoom = map.getZoom();
    const centre = map.unproject(map.project(point, zoom).add([0, covered / 2]), zoom);

    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      map.setView(centre, zoom, { animate: false });
      return;
    }

    // A move longer than the map itself is a flight: flyTo zooms out part of the
    // way while it moves and back in to the same zoom, so the visitor sees where
    // the map is going, and its duration grows with the distance. A shorter move
    // is a pan with a longer duration and a gentler ease-out than Leaflet's
    // defaults, which cover any distance in 0.25 s.
    const distance = map.latLngToContainerPoint(centre).distanceTo(size.divideBy(2));

    if (distance <= Math.max(size.x, size.y)) {
      map.panTo(centre, { duration: PAN_DURATION_S, easeLinearity: PAN_EASE_LINEARITY });
      return;
    }

    // Leaflet draws the shapes only for the area around the view where the last
    // move ended, and draws again when the next move ends. A flight ends only when
    // it lands, and its view leaves that area on the way, so without this the
    // shapes vanish and only the labels fly. Redrawing on every frame of the
    // flight keeps them on screen. `_reset` is Leaflet's private redraw, the one it
    // runs after a jump to a new view; nothing public redraws at a zoom that is
    // still changing. The hit layers are taken off the map for the flight, so each
    // frame redraws half as many paths: they are invisible, and nothing can be
    // clicked in mid-flight.
    const renderer = RENDERER as unknown as { _reset: () => void };
    const hitLayer = hitLayerRef.current;
    // Labels are placed again on every frame as well, so the zoom change in the
    // middle of the flight does not leave them overlapping until it lands.
    const redraw = () => {
      renderer._reset();
      placeLabelsRef.current?.();
    };
    let flying = true;

    const land = () => {
      if (!flying) {
        return;
      }

      flying = false;
      map.off('zoom', redraw);
      map.off('moveend', land);

      if (hitLayer && hitLayerRef.current === hitLayer) {
        map.addLayer(hitLayer);
      }
    };

    if (hitLayer) {
      map.removeLayer(hitLayer);
    }

    // flyTo fires `zoom` on every frame, and `moveend` when it lands. A drag or
    // another selection during the flight stops it without `moveend`; the
    // cleanup below covers the second case, and the drag's own `moveend` the
    // first.
    map.on('zoom', redraw);
    map.on('moveend', land);
    map.flyTo(centre, zoom);

    return land;
  }, [map, selectedJisCode, obscuredBottomPx]);

  return null;
}

/**
 * Keeps Leaflet's idea of the map size in step with the element.
 *
 * Leaflet measures its container once and then on window resize only. The map's
 * box also changes size without a window resize: when the viewport crosses the
 * breakpoint and the layout changes, and when a phone changes orientation. A
 * ResizeObserver catches every one of those.
 */
function SizeWatcher() {
  const map = useMap();

  useEffect(() => {
    const container = map.getContainer();
    (window as unknown as { __map: L.Map; __renderer: L.Renderer }).__map = map;
    const observer = new ResizeObserver(() => {
      // A zero-sized container means the map is hidden. Invalidating then would
      // record 0×0 as the size and leave it there.
      if (container.clientWidth > 0 && container.clientHeight > 0) {
        map.invalidateSize({ animate: false });
      }
    });

    observer.observe(container);
    return () => observer.disconnect();
  }, [map]);

  return null;
}

/**
 * The JIS code of the prefecture whose geometry contains the point, or null for a
 * point in the sea.
 *
 * Ray casting in degrees, over the same GeoJSON the map draws. Degrees are not
 * a planar projection, but containment does not depend on the projection: a
 * point inside a ring is inside it in any coordinate system that keeps the edges
 * in order. The bounding box check skips most of the 47 prefectures before any
 * ring is read.
 */
function prefectureAt(
  outlines: readonly { readonly feature: PrefectureFeature; readonly box: BoundingBox }[],
  point: L.LatLng
): number | null {
  const { lng: x, lat: y } = point;

  for (const { feature, box } of outlines) {
    const [west, south, east, north] = box;

    if (x < west || x > east || y < south || y > north) {
      continue;
    }

    for (const [outer, ...holes] of feature.geometry.coordinates) {
      if (outer && ringContains(outer, x, y) && !holes.some((hole) => ringContains(hole, x, y))) {
        return feature.properties.jisCode;
      }
    }
  }

  return null;
}

/** Even-odd rule: a ray from the point crosses the ring an odd number of times. */
function ringContains(ring: readonly (readonly number[])[], x: number, y: number): boolean {
  let inside = false;

  for (let index = 0, previous = ring.length - 1; index < ring.length; previous = index++) {
    const [xi = 0, yi = 0] = ring[index] ?? [];
    const [xj = 0, yj = 0] = ring[previous] ?? [];

    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) {
      inside = !inside;
    }
  }

  return inside;
}

/** West, south, east, north, in degrees. */
type BoundingBox = readonly [number, number, number, number];

/** The box around every outer ring of a prefecture. */
function boundingBox(feature: PrefectureFeature): BoundingBox {
  let [west, south, east, north] = [Infinity, Infinity, -Infinity, -Infinity];

  for (const [x = 0, y = 0] of feature.geometry.coordinates.flatMap(
    (polygon) => polygon[0] ?? []
  )) {
    west = Math.min(west, x);
    east = Math.max(east, x);
    south = Math.min(south, y);
    north = Math.max(north, y);
  }

  return [west, south, east, north];
}

/** A label's box in layer pixels, with the gap to its neighbours included. */
interface LabelBox {
  readonly left: number;
  readonly right: number;
  readonly top: number;
  readonly bottom: number;
}

/**
 * The smallest space between two labels, in rem. Without a gap, two names can
 * touch and read as one name.
 */
const LABEL_GAP_REM = 0.25;

function overlaps(a: LabelBox, b: LabelBox): boolean {
  return a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;
}

/** 1rem in CSS pixels: the computed font size of the root element. */
function rootFontSizePx(): number {
  return Number.parseFloat(getComputedStyle(document.documentElement).fontSize);
}

/**
 * Attaches every label, reads the size of its text, and leaves them attached.
 * `placeLabels` removes the ones that do not fit straight after.
 */
function measureLabels(
  labels: ReadonlyMap<number, L.Marker>,
  labelLayer: L.LayerGroup
): Map<number, { readonly width: number; readonly height: number }> {
  const sizes = new Map<number, { readonly width: number; readonly height: number }>();

  for (const [jisCode, marker] of labels) {
    labelLayer.addLayer(marker);
    const text = marker.getElement()?.firstElementChild;

    if (text instanceof HTMLElement) {
      sizes.set(jisCode, { width: text.offsetWidth, height: text.offsetHeight });
    }
  }

  return sizes;
}

/**
 * How far, in pixels, the pointer may move between press and release for Leaflet
 * to count a click rather than a drag.
 *
 * Leaflet's default is 3. A press on a laptop trackpad often moves the pointer by
 * 3 pixels or more, and Leaflet then fires no click, so the prefecture under the
 * pointer is not selected. A drag shorter than this does not move the map.
 */
const CLICK_TOLERANCE = 6;

/**
 * Sets {@link CLICK_TOLERANCE} on the map's drag handler.
 *
 * Neither `MapContainer` nor `L.Map` takes this as an option; it belongs to the
 * `L.Draggable` that `map.dragging` creates when the map is built, which Leaflet
 * does not expose in its types.
 */
function ClickTolerance() {
  const map = useMap();

  useEffect(() => {
    const draggable = (map.dragging as unknown as { _draggable?: L.Draggable })._draggable;

    if (draggable) {
      L.Util.setOptions(draggable, { clickTolerance: CLICK_TOLERANCE });
    }
  }, [map]);

  return null;
}

function shapeStyle(coverage: Coverage, selected: boolean): L.PathOptions {
  const style = COVERAGE[coverage];

  return {
    fillColor: style.fill,
    fillOpacity: selected ? FILL_OPACITY_SELECTED : FILL_OPACITY_DEFAULT,
    color: selected ? SELECTED_STROKE : style.stroke,
    weight: selected ? STROKE_WEIGHT_SELECTED : STROKE_WEIGHT_DEFAULT,
    opacity: 1,
    lineJoin: 'round'
  };
}

/**
 * Relative size of a prefecture, used only to order the hit layers.
 *
 * The shoelace formula over the outer rings, in square degrees. Degrees are not
 * an area unit and this ignores that a degree of longitude narrows towards the
 * poles, which is fine: nothing reads the number, it is only ever compared with
 * another one from the same 20 degrees of latitude. It is measured over the real
 * polygons rather than the bounding box because Tokyo's box spans a thousand
 * kilometres of ocean while Tokyo itself is one of the smallest prefectures —
 * exactly the case this ordering exists to get right.
 */
function approximateArea(feature: PrefectureFeature): number {
  let total = 0;

  for (const polygon of feature.geometry.coordinates) {
    const ring = polygon[0];

    if (!ring) {
      continue;
    }

    let doubleArea = 0;

    for (let index = 0; index < ring.length; index += 1) {
      const current = ring[index];
      const next = ring[(index + 1) % ring.length];

      if (!current || !next) {
        continue;
      }

      // A GeoJSON position always carries at least a longitude and a latitude.
      // The defaults are here because the type says `number[]`, and this number
      // is only ever compared with another one from the same file.
      const [x1 = 0, y1 = 0] = current;
      const [x2 = 0, y2 = 0] = next;

      doubleArea += x1 * y2 - x2 * y1;
    }

    total += Math.abs(doubleArea) / 2;
  }

  return total;
}
