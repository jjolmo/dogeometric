# Ground truth for Dogeometric's .skp reader: run inside SketchUp (SketchUp.exe -RubyStartup oracle.rb), it opens
# every .skp next to this script, counts entities with SketchUp's own Ruby API and writes oracle.json.
# Counts are per definition (not flattened), matching how OpenSKP and Dogeometric store models.
require 'json'

module DogeometricOracle
  DIR = File.dirname(File.expand_path(__FILE__))

  def self.count(entities, acc)
    entities.each do |e|
      case e
      when Sketchup::Edge then acc['edges'] += 1
      when Sketchup::Face then acc['faces'] += 1
      when Sketchup::Group then acc['instances'] += 1; acc['groups'] += 1
      when Sketchup::ComponentInstance then acc['instances'] += 1
      when Sketchup::Image then acc['images'] += 1
      when Sketchup::ConstructionLine then acc['guides'] += 1
      when Sketchup::ConstructionPoint then acc['guide_points'] += 1
      when Sketchup::Text then acc['texts'] += 1
      when Sketchup::Dimension then acc['dimensions'] += 1
      when Sketchup::SectionPlane then acc['section_planes'] += 1
      end
    end
    vertices = {}
    entities.grep(Sketchup::Edge).each { |edge| edge.vertices.each { |v| vertices[v.entityID] = true } }
    acc['vertices'] += vertices.size
  end

  def self.stats(model)
    acc = Hash.new(0)
    count(model.entities, acc)
    model.definitions.each do |d|
      next if d.image?
      acc['definitions'] += 1
      acc['group_definitions'] += 1 if d.group?
      count(d.entities, acc)
    end
    acc['materials'] = model.materials.size
    acc['textures'] = model.materials.count(&:texture)
    acc['layers'] = model.layers.size
    acc['pages'] = model.pages.size
    acc['units'] = model.options['UnitsOptions']['LengthUnit']
    bb = model.bounds
    acc['bounds_mm'] = mm(bb)
    # Edges and instances only: model.bounds also covers dimensions, text and guides, which Dogeometric doesn't
    # import yet. Instances contribute their transformed definition box, as Entities.Bounds() does.
    acc['geometry_bounds_mm'] = mm(geometry_bounds(model.entities, {}))
    acc['face_camera'] = model.definitions.any? { |d| d.behavior.always_face_camera? && d.instances.any? }
    # First top-level instances: where SketchUp places them (to compare with what we wrote).
    acc['top_instances'] = model.entities.grep(Sketchup::ComponentInstance).first(12).map do |i|
      t = i.transformation
      { 'def' => i.definition.name, 'origin_mm' => t.origin.to_a.map { |v| v.to_mm.round(2) },
        'xaxis' => t.xaxis.to_a.map { |v| v.round(4) }, 'dc' => !i.definition.attribute_dictionary('dynamic_attributes').nil?,
        'glue' => !i.glued_to.nil? }
    end
    acc
  end

  def self.mm(bb)
    bb.empty? ? [] : [bb.min.x.to_mm, bb.min.y.to_mm, bb.min.z.to_mm, bb.max.x.to_mm, bb.max.y.to_mm, bb.max.z.to_mm].map { |v| v.round(2) }
  end

  def self.geometry_bounds(entities, cache)
    bb = Geom::BoundingBox.new
    entities.each do |e|
      case e
      when Sketchup::Edge
        bb.add(e.start.position, e.end.position)
      when Sketchup::Group, Sketchup::ComponentInstance
        d = e.definition
        inner = (cache[d] ||= geometry_bounds(d.entities, cache))
        next if inner.empty?
        (0..7).each { |i| bb.add(inner.corner(i).transform(e.transformation)) }
      end
    end
    bb
  end

  # Waits for the model passed on the command line to load, appends its counts to oracle.jsonl and exits the
  # process at once (exit! skips every "save changes?" prompt). One SketchUp launch per file, see run-oracle.sh.
  def self.run_one
    model = Sketchup.active_model
    path = model.path
    if path.nil? || path.empty?
      UI.start_timer(2, false) { run_one }
      return
    end
    begin
      result = stats(model)
    rescue Exception => ex
      result = { 'error' => "#{ex.class}: #{ex.message}" }
    end
    result['file'] = path
    File.open(File.join(DIR, 'oracle.jsonl'), 'a') { |f| f.puts(JSON.generate(result)) }
    exit!(0)
  end
end

UI.start_timer(3, false) { DogeometricOracle.run_one }
