import L from 'leaflet';
import { useEffect, useRef } from 'react';
import { MapContainer, useMap } from 'react-leaflet';
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
      // Panning is allowed out to the real extent of the geometry, so Tokyo's far
      // islands are reachable even though nothing starts there.
      maxBounds={PANNABLE_BOUNDS}
      maxBoundsViscosity={MAX_BOUNDS_VISCOSITY}
      minZoom={MIN_ZOOM}
      maxZoom={MAX_ZOOM}
      // The footer carries the MLIT attribution in full, including the
      // processing declaration its terms require. That text is far longer than a
      // corner control, so Leaflet's own attribution control is turned off rather
      // than left showing a shorter, non-compliant version of the same thing.
      attributionControl={false}
      // Pinch and double-tap zoom stay on; the buttons are hidden because on a
      // 375px screen they cover map the visitor could be tapping instead.
      zoomControl={false}
      // Leaflet's default is to require ctrl/cmd with the wheel only when
      // scroll-through is a risk. The map fills its own pane and the page does not
      // scroll behind it, so a plain wheel zoom is not a trap here.
      scrollWheelZoom
      aria-label="Choropleth map of Japan's 47 prefectures, coloured by how complete their mascot data is. The prefecture list beside it carries the same information as text."
    >
      <PrefectureLayers {...props} />
      <SizeWatcher />
    </MapContainer>
  );
}

/** Yonaguni in the south-west to eastern Hokkaidō in the north-east. */
const INITIAL_BOUNDS: L.LatLngBoundsExpression = [
  [24.0, 122.9],
  [45.7, 146.1]
];

/** The full extent of the geometry, padded. See the note on Tokyo above. */
const PANNABLE_BOUNDS: L.LatLngBoundsExpression = [
  [20.0, 118.0],
  [47.5, 155.5]
];

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
 * How firmly the map resists a drag past {@link PANNABLE_BOUNDS}. 0 lets it move
 * freely and spring back on release; 1 is a hard edge the drag cannot cross.
 * Just under 1 leaves the edge slightly soft, so a drag there still moves the map
 * a little rather than appearing to be ignored.
 */
const MAX_BOUNDS_VISCOSITY = 0.9;

/**
 * Margin kept around a selected prefecture when the map pans to it, in pixels,
 * so it does not end up against an edge. `obscuredBottomPx` is added to the
 * bottom, which is what keeps a selection out from under the bottom sheet.
 */
const SELECTION_PADDING = 24;

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
  labelMinZoom,
  obscuredBottomPx
}: PrefectureMapProps) {
  const map = useMap();

  // The drawn polygon for each prefecture, so a selection change restyles two
  // layers instead of rebuilding the collection.
  const shapesRef = useRef(new Map<number, L.GeoJSON>());
  // The name label for each prefecture, and where it sits.
  const labelsRef = useRef(new Map<number, L.Marker>());
  const labelPointsRef = useRef(new Map<number, L.LatLng>());

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
  // be re-evaluated without duplicating the rule for which of them are shown.
  const syncLabelsRef = useRef<(() => void) | null>(null);

  // Build the layers. Runs on mount and again when the collection is replaced,
  // which happens when the viewport crosses the breakpoint and the app refetches
  // at a different detail level.
  useEffect(() => {
    const shapes = shapesRef.current;
    const labels = labelsRef.current;
    const labelPoints = labelPointsRef.current;

    const shapeLayer = L.layerGroup().addTo(map);
    const hitLayer = L.layerGroup().addTo(map);
    const labelLayer = L.layerGroup().addTo(map);

    // Largest first, so the smallest prefectures are last in the SVG and their
    // hit strokes sit on top of their neighbours'. A 20px stroke reaches well
    // outside its own prefecture, and without this ordering Hyōgo's stroke would
    // cover a good part of the target this technique exists to give Ōsaka.
    const features = [...collection.features].sort(
      (left, right) => approximateArea(right) - approximateArea(left)
    );

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

      hit.on('click', (event) => {
        // Otherwise the click continues to the map and the background handler
        // below immediately clears the selection this one just made.
        L.DomEvent.stopPropagation(event);
        onSelectRef.current(jisCode);
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

    // Tapping the sea clears the selection, which is how a bottom sheet is
    // dismissed without a close button being the only way.
    const clearSelection = () => onSelectRef.current(null);
    map.on('click', clearSelection);

    // Labels are shown from a zoom threshold rather than always, because 47 of
    // them at the full-Japan zoom on a phone overlap into noise.
    const syncLabels = () => {
      const showAll = map.getZoom() >= labelMinZoom;

      for (const [jisCode, marker] of labels) {
        // Below the threshold the selected prefecture is still labelled, so a tap
        // always names what it selected even when nothing else is named.
        const visible = showAll || jisCode === selectedRef.current;
        const attached = labelLayer.hasLayer(marker);

        if (visible && !attached) {
          labelLayer.addLayer(marker);
        } else if (!visible && attached) {
          labelLayer.removeLayer(marker);
        }
      }
    };

    syncLabels();
    map.on('zoomend', syncLabels);
    syncLabelsRef.current = syncLabels;

    return () => {
      map.off('click', clearSelection);
      map.off('zoomend', syncLabels);
      syncLabelsRef.current = null;
      map.removeLayer(shapeLayer);
      map.removeLayer(hitLayer);
      map.removeLayer(labelLayer);
      shapes.clear();
      labels.clear();
      labelPoints.clear();
    };
  }, [map, collection, labelMinZoom]);

  // Restyle on a selection or coverage change. Two setStyle calls in the common
  // case — the prefecture being deselected and the one being selected.
  useEffect(() => {
    for (const [jisCode, shape] of shapesRef.current) {
      shape.setStyle(shapeStyle(coverageFor(jisCode), selectedJisCode === jisCode));
    }

    syncLabelsRef.current?.();
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

    // panInside moves only when the point is not already comfortably in view, so
    // tapping a prefecture on the map does not shift the map under the finger
    // that tapped it. The bottom padding is the strip the sheet covers, which is
    // why a selection made behind the sheet rises above it.
    map.panInside(point, {
      paddingTopLeft: [SELECTION_PADDING, SELECTION_PADDING],
      paddingBottomRight: [SELECTION_PADDING, obscuredBottomPx + SELECTION_PADDING]
    });
  }, [map, selectedJisCode, obscuredBottomPx]);

  return null;
}

/**
 * Keeps Leaflet's idea of the map size in step with the element.
 *
 * Leaflet measures its container once and then on window resize only. On a narrow
 * viewport this app hides the map to show the list, and a hidden element has no
 * size — so without this the map would come back rendered into a zero-sized box.
 * A ResizeObserver catches that, and the orientation change and the appearance of
 * a mobile browser's toolbar as well.
 */
function SizeWatcher() {
  const map = useMap();

  useEffect(() => {
    const container = map.getContainer();
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
